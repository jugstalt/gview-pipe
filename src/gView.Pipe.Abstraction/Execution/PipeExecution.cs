using gView.Pipe.Abstraction.Data;
using Microsoft.Extensions.Logging;

namespace gView.Pipe.Abstraction.Execution;

/// <summary>One run of a workflow.</summary>
public sealed class PipeExecution
{
    public string Id { get; set; } = "";
    public string WorkflowId { get; set; } = "";
    public string WorkflowName { get; set; } = "";
    public PipeExecutionMode Mode { get; set; }
    public PipeExecutionStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? Error { get; set; }
    public string? ErrorNodeId { get; set; }

    /// <summary>False when the workflow is configured not to store item data.</summary>
    public bool DataSaved { get; set; } = true;

    /// <summary>Runs per node id.</summary>
    public Dictionary<string, PipeNodeRun> NodeRuns { get; set; } = new();
}

public sealed class PipeNodeRun
{
    public string NodeId { get; set; } = "";
    public string NodeName { get; set; } = "";
    public PipeNodeRunStatus Status { get; set; } = PipeNodeRunStatus.Waiting;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }

    /// <summary>Input items per port, as the node received them (null when data is not saved).</summary>
    public Dictionary<string, List<PipeItem>>? Inputs { get; set; }

    public Dictionary<string, int> InputCounts { get; set; } = new();

    /// <summary>Output items per port (null when data is not saved).</summary>
    public Dictionary<string, List<PipeItem>>? Outputs { get; set; }

    public Dictionary<string, int> OutputCounts { get; set; } = new();

    /// <summary>What the node logged during this run, plus the engine's messages about it (retries, errors).</summary>
    public List<PipeLogEntry> Logs { get; set; } = new();
}

public sealed record PipeLogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message);

public enum PipeExecutionMode
{
    Manual,
    Trigger
}

public enum PipeExecutionStatus
{
    Running,
    Success,
    Error,
    Canceled
}

public enum PipeNodeRunStatus
{
    Waiting,
    Running,
    Success,
    Error,
    Skipped,
    Disabled,
    Canceled
}
