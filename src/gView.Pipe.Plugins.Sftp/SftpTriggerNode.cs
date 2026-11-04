using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Triggers;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Sftp;

public enum SftpAfterProcessing
{
    /// <summary>The files stay. Only files that are new after the first check are reported.</summary>
    Nothing,

    /// <summary>Every file counts; after the run has started it is moved to the target folder.</summary>
    MoveToFolder,

    /// <summary>Every file counts; after the run has started it is deleted.</summary>
    Delete
}

public enum SftpTriggerEvents
{
    Created,
    CreatedOrChanged
}

public sealed class SftpTriggerNodeParameters
{
    [Required, PipeCredential(SftpCredential.TypeId)]
    [PipeParameter(Label = "Server", Order = 1, Expressions = false, Description = "SFTP Server credential")]
    public string? CredentialId { get; set; }

    [Required, PipeParameter(Label = "Path", Order = 2, Expressions = false, Placeholder = "/outgoing", Description = "Folder on the server")]
    public string Path { get; set; } = "";

    [PipeParameter(Label = "File pattern", Order = 3, Expressions = false, Placeholder = "*.csv", Description = "* and ? are allowed; capitals don't matter")]
    public string Pattern { get; set; } = "*";

    [PipeParameter(Label = "Include subfolders", Order = 4, Expressions = false)]
    public bool IncludeSubfolders { get; set; }

    [PipeParameter(Label = "After processing", Order = 5, Expressions = false,
        Description = "What happens to a file once the run has started. Move and Delete take every file, also those that were there before; Nothing only reports files that are new")]
    public SftpAfterProcessing AfterProcessing { get; set; } = SftpAfterProcessing.MoveToFolder;

    [PipeParameter(Label = "Target folder", Order = 6, Description = "Move to folder: a folder on the server; created if it doesn't exist")]
    public string? TargetFolder { get; set; }

    [PipeParameter(Label = "Events", Order = 7, Expressions = false, Description = "Nothing: report only new files, or changed ones too")]
    public SftpTriggerEvents Events { get; set; } = SftpTriggerEvents.Created;

    [PipeParameter(Label = "Check every (seconds)", Order = 8, Expressions = false,
        Description = "A file is reported when it was unchanged in two checks in a row, so it comes after up to two intervals")]
    [Range(10, 86400)]
    public int IntervalSeconds { get; set; } = 60;
}

[PipeNode(SftpTriggerNode.NodeId, "SFTP Trigger",
    Category = "Triggers",
    Description = "Starts the workflow when files appear in a folder on an SFTP server, while the workflow is active. "
                  + "Checks at an interval and reports a file once it is complete. One run per check, with one item per file like the List of the SFTP node. "
                  + "By default the files are moved to a target folder once the run has started, so the server holds the state; the SFTP node's Download fetches the content.")]
public sealed class SftpTriggerNode : IPipeTriggerNode, IPipeNode<SftpTriggerNodeParameters>
{
    public const string NodeId = "a0b3ee21-1108-4a67-ac21-d7cb5ce99ad8";

    private readonly ISftpConnector _connector;

    public SftpTriggerNode(ISftpConnector connector)
    {
        _connector = connector;
    }

    public Task<IAsyncDisposable?> ActivateAsync(IPipeTriggerContext context, CancellationToken cancellationToken)
    {
        var parameters = context.GetParameters<SftpTriggerNodeParameters>();
        if (string.IsNullOrWhiteSpace(parameters.CredentialId))
        {
            throw new PipeException("Select the SFTP Server credential");
        }
        RemotePath.Required(parameters.Path, "Path");
        if (parameters.AfterProcessing == SftpAfterProcessing.MoveToFolder && string.IsNullOrWhiteSpace(parameters.TargetFolder))
        {
            throw new PipeException("Move to folder needs a target folder");
        }

        var watcher = new SftpFolderWatcher(_connector, context, parameters);
        var cancellation = new CancellationTokenSource();
        var loop = RunAsync(watcher, TimeSpan.FromSeconds(Math.Clamp(parameters.IntervalSeconds, 10, 86400)), cancellation.Token);
        return Task.FromResult<IAsyncDisposable?>(new Registration(cancellation, loop));
    }

    static private async Task RunAsync(SftpFolderWatcher watcher, TimeSpan interval, CancellationToken cancellationToken)
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

/// <summary>One check of a folder on the server per call (ADR 0039). Keeps its state in memory only.</summary>
public sealed class SftpFolderWatcher
{
    /// <summary>At most this many files per run.</summary>
    public const int MaxPerRun = 100;

    private const int MaxListed = 10000;
    private const int MaxRemembered = 1000;

    private readonly ISftpConnector _connector;
    private readonly IPipeTriggerContext _context;
    private readonly SftpTriggerNodeParameters _parameters;

    private readonly record struct Signature(long Size, DateTimeOffset ModifiedAt);

    // files seen once, and the signature they had
    private readonly Dictionary<string, Signature> _pending = new(StringComparer.Ordinal);
    // After processing = Nothing: files that are known and don't trigger again unless they change
    private readonly Dictionary<string, Signature> _known = new(StringComparer.Ordinal);
    // Move/Delete: files that started a run but could not be moved or deleted; they don't start a second one
    private readonly HashSet<string> _handedOn = new(StringComparer.Ordinal);
    private bool _baselineTaken;

    public SftpFolderWatcher(ISftpConnector connector, IPipeTriggerContext context, SftpTriggerNodeParameters parameters)
    {
        _connector = connector;
        _context = context;
        _parameters = parameters;
    }

    private bool KeepsFiles => _parameters.AfterProcessing == SftpAfterProcessing.Nothing;

    /// <summary>Connects, lists the folder, starts a run for the files that are complete and handles them. Errors are logged, so the next check goes on.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var credential = await _context.GetCredentialAsync(_parameters.CredentialId!, cancellationToken);
            var server = credential.Data.Deserialize<SftpCredential>(PipeJson.Options) ?? new SftpCredential();
            if (string.IsNullOrWhiteSpace(server.HostKeyFingerprint))
            {
                throw new PipeException("The credential has no host key fingerprint; without it the server can't be checked");
            }

            await using var connection = await _connector.ConnectAsync(server, TimeSpan.FromSeconds(30), cancellationToken);
            var folder = RemotePath.Required(_parameters.Path, "Path");
            var entries = await connection.ListAsync(folder, _parameters.IncludeSubfolders, MaxListed, cancellationToken);

            var ready = Poll(entries);
            if (ready.Count == 0)
            {
                return;
            }

            var items = ready.Select(change => ToItem(change.Entry, change.Event)).ToList();
            // null: the workflow was busy and its queue full; the files stay and come with the next check
            if (await _context.FireAsync(items, cancellationToken) is null)
            {
                return;
            }

            if (KeepsFiles)
            {
                foreach (var change in ready)
                {
                    _known[change.Entry.Path] = new Signature(change.Entry.Size, change.Entry.ModifiedAt);
                    _pending.Remove(change.Entry.Path);
                }
                return;
            }

            await HandleAsync(connection, ready.Select(r => r.Entry).ToList(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _context.Logger.LogError(ex, "The check of the folder on the server failed");
        }
    }

    private List<(SftpEntry Entry, string Event)> Poll(IReadOnlyList<SftpEntry> listed)
    {
        var pattern = SftpNode.PatternToRegex(_parameters.Pattern);
        var current = listed
            .Where(e => !e.IsDirectory && pattern.IsMatch(e.Name)
                        && !e.Name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) && !e.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(e => e.Path, StringComparer.Ordinal);

        if (KeepsFiles && !_baselineTaken)
        {
            _baselineTaken = true;
            foreach (var entry in current.Values)
            {
                _known[entry.Path] = new Signature(entry.Size, entry.ModifiedAt);
            }
            return [];
        }

        // gone files are forgotten, so one that comes back is new
        foreach (var gone in _known.Keys.Where(k => !current.ContainsKey(k)).ToList())
        {
            _known.Remove(gone);
        }
        foreach (var gone in _pending.Keys.Where(k => !current.ContainsKey(k)).ToList())
        {
            _pending.Remove(gone);
        }
        _handedOn.RemoveWhere(key => !current.Values.Any(e => KeyOf(e) == key));

        var ready = new List<(SftpEntry, string)>();
        foreach (var entry in current.Values.OrderBy(e => e.ModifiedAt).ThenBy(e => e.Path, StringComparer.Ordinal))
        {
            var signature = new Signature(entry.Size, entry.ModifiedAt);
            var isKnown = _known.TryGetValue(entry.Path, out var knownSignature);
            if (isKnown && knownSignature == signature)
            {
                _pending.Remove(entry.Path);
                continue;
            }
            if (isKnown && _parameters.Events == SftpTriggerEvents.Created)
            {
                // changed, but only new files count
                _known[entry.Path] = signature;
                _pending.Remove(entry.Path);
                continue;
            }
            if (_handedOn.Contains(KeyOf(entry)))
            {
                continue;
            }

            // two equal checks in a row make a file complete
            if (!_pending.TryGetValue(entry.Path, out var seen) || seen != signature)
            {
                _pending[entry.Path] = signature;
                continue;
            }

            if (ready.Count < MaxPerRun)
            {
                ready.Add((entry, isKnown ? "changed" : "created"));
            }
        }
        return ready;
    }

    private async Task HandleAsync(ISftpConnection connection, IReadOnlyList<SftpEntry> files, CancellationToken cancellationToken)
    {
        if (_handedOn.Count > MaxRemembered)
        {
            _handedOn.Clear();
        }

        var failed = 0;
        foreach (var file in files)
        {
            _pending.Remove(file.Path);
            _handedOn.Add(KeyOf(file));
            try
            {
                if (_parameters.AfterProcessing == SftpAfterProcessing.Delete)
                {
                    await connection.DeleteAsync(file.Path, cancellationToken);
                }
                else
                {
                    var target = RemotePath.Required(_parameters.TargetFolder, "Target folder").TrimEnd('/');
                    await SftpNode.EnsureFolderAsync(connection, target, create: true, cancellationToken);
                    await connection.RenameAsync(file.Path, await UniqueTargetAsync(connection, target, file.Name, cancellationToken), cancellationToken);
                }
                _handedOn.Remove(KeyOf(file));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                _context.Logger.LogError(ex, "The file {Path} started a run but could not be {Action}; this trigger doesn't start it again", file.Path, _parameters.AfterProcessing);
            }
        }

        if (failed > 0)
        {
            _context.Logger.LogWarning("{Count} of {Total} file(s) could not be handled", failed, files.Count);
        }
    }

    // a name that is taken gets _2, _3, … before its extension
    static private async Task<string> UniqueTargetAsync(ISftpConnection connection, string folder, string name, CancellationToken cancellationToken)
    {
        var candidate = $"{folder}/{name}";
        var extension = System.IO.Path.GetExtension(name);
        var stem = name[..^extension.Length];
        for (var n = 2; await connection.StatAsync(candidate, cancellationToken) is not null; n++)
        {
            candidate = $"{folder}/{stem}_{n}{extension}";
        }
        return candidate;
    }

    static private string KeyOf(SftpEntry entry) => $"{entry.Path}|{entry.Size}|{entry.ModifiedAt.UtcTicks}";

    static private PipeItem ToItem(SftpEntry entry, string @event)
        => new(new JsonObject
        {
            ["fileName"] = entry.Name,
            ["path"] = entry.Path,
            ["size"] = entry.Size,
            ["modifiedAt"] = entry.ModifiedAt.ToUniversalTime().ToString("O"),
            ["isDirectory"] = false,
            ["event"] = @event
        });
}
