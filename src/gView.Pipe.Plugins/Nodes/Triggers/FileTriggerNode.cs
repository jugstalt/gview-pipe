using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Files;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Plugins.Common;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Triggers;

public enum FileTriggerEvents
{
    [PipeEnumValue("Only files that weren't there before", Label = "New files")]
    Created,

    [PipeEnumValue("New files, and files whose size or time changed", Label = "New or changed files")]
    CreatedOrChanged
}

public sealed class FileTriggerNodeParameters
{
    [Required, PipeOptions, PipeParameter(Label = "Folder", Order = 1, Expressions = false,
        Description = "Which folder to watch: one of the folders the administrator has set up for workflows")]
    public string Folder { get; set; } = "";

    [PipeParameter(Label = "Path", Order = 2, Expressions = false, Placeholder = "incoming", Description = "A subfolder of the folder, e.g. incoming; empty for the folder itself")]
    public string Path { get; set; } = "";

    [PipeParameter(Label = "File pattern", Order = 3, Expressions = false, Placeholder = "*.csv", Description = "Which files count, e.g. *.csv or order-*.xml; * for all")]
    public string Pattern { get; set; } = "*";

    [PipeParameter(Label = "Include subfolders", Order = 4, Expressions = false, Advanced = true,
        Description = "Watch the subfolders of the path too")]
    public bool IncludeSubfolders { get; set; }

    [PipeParameter(Label = "Events", Order = 5, Expressions = false, Description = "Which files start the workflow")]
    public FileTriggerEvents Events { get; set; } = FileTriggerEvents.Created;

    [PipeParameter(Label = "Check every (seconds)", Order = 6, Expressions = false, Advanced = true,
        Description = "A file is reported when it was unchanged in two checks in a row, so it comes after up to two intervals")]
    [Range(1, 86400)]
    public int IntervalSeconds { get; set; } = 10;
}

[PipeNodeExamples]
[PipeNode(PipeBuiltInNodes.FileTrigger, "File Trigger",
    Category = "Triggers",
    Description = "Starts the workflow when new files appear in a folder released by the host, while the workflow is active. "
                  + "Checks the folder at an interval and reports a file once it is complete. One run per check, with one item per file: "
                  + "folder, path, name, size, modified and event. Files that exist when the workflow is activated are not reported. "
                  + "Read File reads the content.")]
public sealed class FileTriggerNode : IPipeTriggerNode, IPipeNode<FileTriggerNodeParameters>, IPipeParameterOptions
{
    private readonly IPipeFileRoots _roots;

    public FileTriggerNode(IPipeFileRoots roots)
    {
        _roots = roots;
    }

    public Task<IReadOnlyList<PipeParameterOption>> GetOptionsAsync(string parameter, JsonObject parameters, bool refresh, CancellationToken cancellationToken)
        => Task.FromResult(Files.PipeFileHints.FolderOptions(_roots, parameter));

    public Task<IAsyncDisposable?> ActivateAsync(IPipeTriggerContext context, CancellationToken cancellationToken)
    {
        var parameters = context.GetParameters<FileTriggerNodeParameters>();
        // a wrong folder or path is a problem of the activation
        var directory = _roots.Resolve(parameters.Folder, parameters.Path ?? "");
        var root = _roots.Resolve(parameters.Folder, "");

        var poller = new PipeFolderPoller(
            directory,
            parameters.Pattern,
            parameters.IncludeSubfolders,
            parameters.Events == FileTriggerEvents.CreatedOrChanged,
            message => context.Logger.LogWarning("{Message}", message));
        // the files that are there now are the baseline
        poller.Poll();

        var interval = TimeSpan.FromSeconds(Math.Clamp(parameters.IntervalSeconds, 1, 86400));
        var cancellation = new CancellationTokenSource();
        var loop = RunAsync(context, parameters, poller, root, interval, cancellation.Token);
        return Task.FromResult<IAsyncDisposable?>(new Registration(cancellation, loop));
    }

    private static async Task RunAsync(IPipeTriggerContext context, FileTriggerNodeParameters parameters, PipeFolderPoller poller,
        string root, TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await CheckAsync(context, parameters, poller, root, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>One check: the new files become the items of one run.</summary>
    static internal async Task CheckAsync(IPipeTriggerContext context, FileTriggerNodeParameters parameters, PipeFolderPoller poller,
        string root, CancellationToken cancellationToken)
    {
        try
        {
            var changes = poller.Poll();
            if (changes.Count == 0)
            {
                return;
            }

            var items = changes.Select(change => ToItem(parameters.Folder, root, change)).ToList();
            // null: the workflow was busy and its queue full; the files stay waiting and come with the next check
            if (await context.FireAsync(items, cancellationToken) is not null)
            {
                poller.Commit(changes);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex, "The check of the folder failed");
        }
    }

    static private PipeItem ToItem(string folder, string root, PipeFolderChange change)
        => new(new JsonObject
        {
            ["folder"] = folder,
            ["path"] = System.IO.Path.GetRelativePath(root, change.FullPath).Replace('\\', '/'),
            ["name"] = System.IO.Path.GetFileName(change.FullPath),
            ["size"] = change.Size,
            ["modified"] = new DateTimeOffset(change.ModifiedUtc, TimeSpan.Zero).ToString("O"),
            ["event"] = change.Event
        });

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
