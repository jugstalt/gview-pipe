using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Triggers;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace gView.Pipe.Plugins.AzureStorage;

public enum AzureBlobAfterProcessing
{
    /// <summary>The blobs stay. Only blobs that are new after the first check are reported.</summary>
    Nothing,

    /// <summary>Every blob counts; after the run has started it is moved to the target folder.</summary>
    MoveToFolder,

    /// <summary>Every blob counts; after the run has started it is deleted.</summary>
    Delete
}

public enum AzureBlobTriggerEvents
{
    Created,
    CreatedOrChanged
}

public sealed class AzureBlobTriggerNodeParameters
{
    [Required, PipeCredential(AzureStorageCredential.TypeId)]
    [PipeParameter(Label = "Storage account", Order = 1, Expressions = false, Description = "Azure Storage credential")]
    public string? CredentialId { get; set; }

    [Required, PipeParameter(Label = "Container", Order = 2, Expressions = false, Placeholder = "incoming")]
    public string Container { get; set; } = "";

    [PipeParameter(Label = "Path", Order = 3, Expressions = false, Placeholder = "orders", Description = "A folder in the container; empty for the whole container")]
    public string Path { get; set; } = "";

    [PipeParameter(Label = "File pattern", Order = 4, Expressions = false, Placeholder = "*.csv", Description = "* and ? on the name of the blob; capitals don't matter")]
    public string Pattern { get; set; } = "*";

    [PipeParameter(Label = "Include subfolders", Order = 5, Expressions = false)]
    public bool IncludeSubfolders { get; set; }

    [PipeParameter(Label = "After processing", Order = 6, Expressions = false,
        Description = "What happens to a blob once the run has started. Move and Delete take every blob, also those that were there before; Nothing only reports blobs that are new")]
    public AzureBlobAfterProcessing AfterProcessing { get; set; } = AzureBlobAfterProcessing.MoveToFolder;

    [PipeParameter(Label = "Target container", Order = 7, Expressions = false, Description = "Move to folder: empty for the same container; created if it doesn't exist")]
    public string? TargetContainer { get; set; }

    [PipeParameter(Label = "Target folder", Order = 8, Expressions = false, Placeholder = "done", Description = "Move to folder: the folder the blobs go to")]
    public string? TargetFolder { get; set; }

    [PipeParameter(Label = "Events", Order = 9, Expressions = false, Description = "Nothing: report only new blobs, or changed ones too")]
    public AzureBlobTriggerEvents Events { get; set; } = AzureBlobTriggerEvents.Created;

    [PipeParameter(Label = "Check every (seconds)", Order = 10, Expressions = false)]
    [Range(10, 86400)]
    public int IntervalSeconds { get; set; } = 60;
}

[PipeNode(AzureBlobTriggerNode.NodeId, "Azure Blob Trigger",
    Category = "Triggers",
    Description = "Starts the workflow when blobs appear in a container of Azure Blob Storage, while the workflow is active. "
                  + "Checks at an interval; one run per check, with one item per blob like the List of the Azure Blob node. "
                  + "By default the blobs are moved to a target folder once the run has started; the Azure Blob node's Download fetches the content.")]
public sealed class AzureBlobTriggerNode : IPipeTriggerNode, IPipeNode<AzureBlobTriggerNodeParameters>
{
    public const string NodeId = "b5ed26a2-4701-4514-b8ec-64a7f6639766";

    private readonly IPipeBlobConnector _connector;

    public AzureBlobTriggerNode(IPipeBlobConnector connector)
    {
        _connector = connector;
    }

    public Task<IAsyncDisposable?> ActivateAsync(IPipeTriggerContext context, CancellationToken cancellationToken)
    {
        var parameters = context.GetParameters<AzureBlobTriggerNodeParameters>();
        if (string.IsNullOrWhiteSpace(parameters.CredentialId))
        {
            throw new PipeException("Select the Azure Storage credential");
        }
        BlobPaths.Container(parameters.Container, "Container");
        if (parameters.AfterProcessing == AzureBlobAfterProcessing.MoveToFolder && string.IsNullOrWhiteSpace(parameters.TargetFolder))
        {
            throw new PipeException("Move to folder needs a target folder");
        }

        var watcher = new AzureBlobWatcher(_connector, context, parameters);
        var cancellation = new CancellationTokenSource();
        var loop = RunAsync(watcher, TimeSpan.FromSeconds(Math.Clamp(parameters.IntervalSeconds, 10, 86400)), cancellation.Token);
        return Task.FromResult<IAsyncDisposable?>(new Registration(cancellation, loop));
    }

    static private async Task RunAsync(AzureBlobWatcher watcher, TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            // the first check is made at once: with After processing = Nothing it takes the baseline
            do
            {
                await watcher.CheckAsync(cancellationToken);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
        => Task.FromResult(PipeNodeResult.FromMain(context.GetInput()));

    private sealed class Registration : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly Task _loop;

        public Registration(CancellationTokenSource cancellation, Task loop)
        {
            _cancellation = cancellation;
            _loop = loop;
        }

        public async ValueTask DisposeAsync()
        {
            _cancellation.Cancel();
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
            }
            _cancellation.Dispose();
        }
    }
}

/// <summary>One check of a container per call (ADR 0040). Keeps its state in memory only.</summary>
public sealed class AzureBlobWatcher
{
    /// <summary>At most this many blobs per run.</summary>
    public const int MaxPerRun = 100;

    private const int MaxListed = 10000;
    private const int MaxRemembered = 1000;

    private readonly IPipeBlobConnector _connector;
    private readonly IPipeTriggerContext _context;
    private readonly AzureBlobTriggerNodeParameters _parameters;

    // After processing = Nothing: blobs that are known and don't trigger again unless they change
    private readonly Dictionary<string, string> _known = new(StringComparer.Ordinal);
    // Move/Delete: blobs that started a run but could not be moved or deleted; they don't start a second one
    private readonly HashSet<string> _handedOn = new(StringComparer.Ordinal);
    private bool _baselineTaken;

    public AzureBlobWatcher(IPipeBlobConnector connector, IPipeTriggerContext context, AzureBlobTriggerNodeParameters parameters)
    {
        _connector = connector;
        _context = context;
        _parameters = parameters;
    }

    private bool KeepsBlobs => _parameters.AfterProcessing == AzureBlobAfterProcessing.Nothing;

    /// <summary>Lists the container, starts a run for the blobs and handles them. Errors are logged, so the next check goes on.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var credential = await _context.GetCredentialAsync(_parameters.CredentialId!, cancellationToken);
            var account = credential.Data.Deserialize<AzureStorageCredential>(PipeJson.Options) ?? new AzureStorageCredential();
            var session = _connector.Connect(account, TimeSpan.FromSeconds(30));

            var container = BlobPaths.Container(_parameters.Container, "Container");
            var listed = await session.ListAsync(container, BlobPaths.Prefix(_parameters.Path), _parameters.IncludeSubfolders, MaxListed, cancellationToken);

            var ready = Select(listed);
            if (ready.Count == 0)
            {
                return;
            }

            var items = ready.Select(r =>
            {
                var json = BlobPaths.Describe(r.Blob);
                json["event"] = r.Event;
                return new PipeItem(json);
            }).ToList();

            // null: the workflow was busy and its queue full; the blobs stay and come with the next check
            if (await _context.FireAsync(items, cancellationToken) is null)
            {
                return;
            }

            if (KeepsBlobs)
            {
                foreach (var (blob, _) in ready)
                {
                    _known[blob.Path] = SignatureOf(blob);
                }
                return;
            }

            await HandleAsync(session, ready.Select(r => r.Blob).ToList(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _context.Logger.LogError(ex, "The check of the container failed");
        }
    }

    // a blob is visible only when it is complete, so one check is enough to report it
    private List<(PipeBlobEntry Blob, string Event)> Select(IReadOnlyList<PipeBlobEntry> listed)
    {
        var pattern = BlobPaths.PatternToRegex(_parameters.Pattern);
        var current = listed.Where(b => pattern.IsMatch(b.Name)).ToDictionary(b => b.Path, StringComparer.Ordinal);

        if (KeepsBlobs && !_baselineTaken)
        {
            _baselineTaken = true;
            foreach (var blob in current.Values)
            {
                _known[blob.Path] = SignatureOf(blob);
            }
            return [];
        }

        // gone blobs are forgotten, so one that comes back is new
        foreach (var gone in _known.Keys.Where(k => !current.ContainsKey(k)).ToList())
        {
            _known.Remove(gone);
        }
        var keys = current.Values.Select(KeyOf).ToHashSet(StringComparer.Ordinal);
        _handedOn.RemoveWhere(key => !keys.Contains(key));

        var ready = new List<(PipeBlobEntry, string)>();
        foreach (var blob in current.Values.OrderBy(b => b.ModifiedAt).ThenBy(b => b.Path, StringComparer.Ordinal))
        {
            var isKnown = _known.TryGetValue(blob.Path, out var known);
            if (isKnown && known == SignatureOf(blob))
            {
                continue;
            }
            if (isKnown && _parameters.Events == AzureBlobTriggerEvents.Created)
            {
                // changed, but only new blobs count
                _known[blob.Path] = SignatureOf(blob);
                continue;
            }
            if (_handedOn.Contains(KeyOf(blob)) || ready.Count >= MaxPerRun)
            {
                continue;
            }
            ready.Add((blob, isKnown ? "changed" : "created"));
        }
        return ready;
    }

    private async Task HandleAsync(IPipeBlobSession session, IReadOnlyList<PipeBlobEntry> blobs, CancellationToken cancellationToken)
    {
        if (_handedOn.Count > MaxRemembered)
        {
            _handedOn.Clear();
        }

        var failed = 0;
        foreach (var blob in blobs)
        {
            _handedOn.Add(KeyOf(blob));
            try
            {
                if (_parameters.AfterProcessing == AzureBlobAfterProcessing.MoveToFolder)
                {
                    var targetContainer = string.IsNullOrWhiteSpace(_parameters.TargetContainer)
                        ? blob.Container
                        : BlobPaths.Container(_parameters.TargetContainer, "Target container");
                    if (targetContainer != blob.Container)
                    {
                        await AzureBlobNode.EnsureContainerAsync(session, targetContainer, create: true, cancellationToken);
                    }
                    var target = await UniqueTargetAsync(session, targetContainer, BlobPaths.Prefix(_parameters.TargetFolder), blob.Name, cancellationToken);
                    await session.CopyAsync(blob.Container, blob.Path, targetContainer, target, overwrite: false, cancellationToken);
                }
                await session.DeleteAsync(blob.Container, blob.Path, cancellationToken);
                _handedOn.Remove(KeyOf(blob));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                _context.Logger.LogError(ex, "The blob {Container}/{Path} started a run but could not be {Action}; this trigger doesn't start it again",
                    blob.Container, blob.Path, _parameters.AfterProcessing);
            }
        }

        if (failed > 0)
        {
            _context.Logger.LogWarning("{Count} of {Total} blob(s) could not be handled", failed, blobs.Count);
        }
    }

    // a name that is taken gets _2, _3, … before its extension
    static private async Task<string> UniqueTargetAsync(IPipeBlobSession session, string container, string folder, string name, CancellationToken cancellationToken)
    {
        var candidate = folder + name;
        var extension = System.IO.Path.GetExtension(name);
        var stem = name[..^extension.Length];
        for (var n = 2; await session.StatAsync(container, candidate, cancellationToken) is not null; n++)
        {
            candidate = $"{folder}{stem}_{n}{extension}";
        }
        return candidate;
    }

    static private string SignatureOf(PipeBlobEntry blob) => blob.ETag ?? $"{blob.Size}|{blob.ModifiedAt.UtcTicks}";

    static private string KeyOf(PipeBlobEntry blob) => $"{blob.Container}/{blob.Path}|{SignatureOf(blob)}";
}
