using gView.Pipe.Abstraction.Execution;
using Microsoft.Extensions.Logging;

namespace gView.Pipe.Core.Engine;

/// <summary>
/// Keeps track of the executions running in this process: registered as observer of every execution, it holds
/// their node runs and live output, and the cancellation of each run (registered by the runner). Finished
/// executions stay visible for <see cref="KeepFinished"/>, so short runs can still be opened.
/// </summary>
internal sealed class PipeExecutionMonitor : IPipeExecutionObserver, IPipeRunningExecutions
{
    static public readonly TimeSpan KeepFinished = TimeSpan.FromSeconds(60);
    private const int MaxLines = 2000;

    private readonly TimeProvider _time;
    private readonly ILogger<PipeExecutionMonitor> _logger;
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly Lock _lock = new();

    public PipeExecutionMonitor(TimeProvider time, ILogger<PipeExecutionMonitor> logger)
    {
        _time = time;
        _logger = logger;
    }

    public event Action<string>? Changed;

    // ---- observer (engine side) ----

    public Task OnExecutionStartedAsync(PipeExecution execution)
    {
        lock (_lock)
        {
            Purge();
            var entry = GetOrAdd(execution.Id);
            entry.Execution = execution;
        }
        Raise(execution.Id);
        return Task.CompletedTask;
    }

    public Task OnNodeStartedAsync(PipeExecution execution, PipeNodeRun run) => NodeChanged(execution, run);

    public Task OnNodeFinishedAsync(PipeExecution execution, PipeNodeRun run) => NodeChanged(execution, run);

    public Task OnNodeLogAsync(PipeExecution execution, PipeNodeRun run, PipeLogEntry entry)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(execution.Id, out var current))
            {
                current.Lines.Enqueue(new PipeLiveLine(run.NodeId, run.NodeName, entry));
                while (current.Lines.Count > MaxLines)
                {
                    current.Lines.Dequeue();
                }
            }
        }
        Raise(execution.Id);
        return Task.CompletedTask;
    }

    public Task OnExecutionFinishedAsync(PipeExecution execution)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(execution.Id, out var entry))
            {
                entry.FinishedAt = _time.GetUtcNow();
                entry.Cancellation = null;
            }
        }
        Raise(execution.Id);
        return Task.CompletedTask;
    }

    private Task NodeChanged(PipeExecution execution, PipeNodeRun run)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(execution.Id, out var entry))
            {
                entry.NodeRuns[run.NodeId] = run;
            }
        }
        Raise(execution.Id);
        return Task.CompletedTask;
    }

    // ---- runner side ----

    /// <summary>Called by the runner before the execution starts, so the UI can stop it.</summary>
    public void RegisterCancellation(string executionId, CancellationTokenSource cancellation)
    {
        lock (_lock)
        {
            GetOrAdd(executionId).Cancellation = cancellation;
        }
    }

    public void UnregisterCancellation(string executionId)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(executionId, out var entry))
            {
                entry.Cancellation = null;
                // the engine never started it (e.g. an invalid workflow): nothing to show
                if (entry.Execution is null)
                {
                    _entries.Remove(executionId);
                }
            }
        }
    }

    // ---- queries (UI side) ----

    public IReadOnlyList<PipeRunningExecutionInfo> List()
    {
        lock (_lock)
        {
            Purge();
            return _entries.Values
                .Where(e => e.Execution is not null)
                .Select(Info)
                .OrderByDescending(i => i.StartedAt)
                .ToList();
        }
    }

    public PipeRunningExecutionInfo? Find(string executionId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(executionId, out var entry) && entry.Execution is not null ? Info(entry) : null;
        }
    }

    public IReadOnlyDictionary<string, PipeNodeRun> GetNodeRuns(string executionId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(executionId, out var entry) ? new Dictionary<string, PipeNodeRun>(entry.NodeRuns) : new Dictionary<string, PipeNodeRun>();
        }
    }

    public PipeExecution? GetFinished(string executionId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(executionId, out var entry) && entry.FinishedAt is not null ? entry.Execution : null;
        }
    }

    public IReadOnlyList<PipeLiveLine> GetLines(string executionId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(executionId, out var entry) ? entry.Lines.ToList() : [];
        }
    }

    public bool Cancel(string executionId)
    {
        CancellationTokenSource? cancellation;
        lock (_lock)
        {
            cancellation = _entries.TryGetValue(executionId, out var entry) && entry.FinishedAt is null ? entry.Cancellation : null;
        }
        if (cancellation is null)
        {
            return false;
        }

        _logger.LogInformation("Execution {ExecutionId} is stopped on request", executionId);
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        return true;
    }

    // ---- helpers (call under the lock) ----

    private Entry GetOrAdd(string executionId)
    {
        if (!_entries.TryGetValue(executionId, out var entry))
        {
            _entries[executionId] = entry = new Entry();
        }
        return entry;
    }

    private void Purge()
    {
        var now = _time.GetUtcNow();
        foreach (var id in _entries.Where(e => e.Value.FinishedAt is { } finished && now - finished > KeepFinished).Select(e => e.Key).ToList())
        {
            _entries.Remove(id);
        }
    }

    static private PipeRunningExecutionInfo Info(Entry entry)
    {
        var execution = entry.Execution!;
        return new PipeRunningExecutionInfo(
            execution.Id,
            execution.WorkflowId,
            execution.WorkflowName,
            execution.Mode,
            entry.FinishedAt is null ? PipeExecutionStatus.Running : execution.Status,
            execution.StartedAt,
            entry.FinishedAt,
            execution.ParentExecutionId,
            entry.NodeRuns.Values.Where(r => r.Status == PipeNodeRunStatus.Running).Select(r => r.NodeName).ToList(),
            entry.Lines.LastOrDefault());
    }

    private void Raise(string executionId)
    {
        try
        {
            Changed?.Invoke(executionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A subscriber of running executions failed");
        }
    }

    private sealed class Entry
    {
        public PipeExecution? Execution { get; set; }
        public Dictionary<string, PipeNodeRun> NodeRuns { get; } = new();
        public Queue<PipeLiveLine> Lines { get; } = new();
        public CancellationTokenSource? Cancellation { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
    }
}
