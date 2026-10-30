namespace gView.Pipe.Abstraction.Execution;

/// <summary>A log entry of a node, received while the execution runs (e.g. a line of a script's output).</summary>
public sealed record PipeLiveLine(string NodeId, string NodeName, PipeLogEntry Entry);

/// <summary>A running (or just finished) execution, as seen at the moment of the call.</summary>
public sealed record PipeRunningExecutionInfo(
    string ExecutionId,
    string WorkflowId,
    string WorkflowName,
    PipeExecutionMode Mode,
    PipeExecutionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? ParentExecutionId,
    IReadOnlyList<string> RunningNodes,
    PipeLiveLine? LastLine)
{
    public bool IsRunning => Status == PipeExecutionStatus.Running;
}

/// <summary>
/// The executions running in this process, whatever started them (editor, list, cron, webhook, sub-workflow,
/// error workflow), plus those that finished in the last minute. For live views: what runs, where it is, its output.
/// </summary>
public interface IPipeRunningExecutions
{
    /// <summary>Running executions and those finished recently, newest first.</summary>
    IReadOnlyList<PipeRunningExecutionInfo> List();

    PipeRunningExecutionInfo? Find(string executionId);

    /// <summary>The node runs so far (a copy of the dictionary; the runs themselves may still change).</summary>
    IReadOnlyDictionary<string, PipeNodeRun> GetNodeRuns(string executionId);

    /// <summary>The execution itself once it has finished (complete with outputs), otherwise null.</summary>
    PipeExecution? GetFinished(string executionId);

    /// <summary>Live output lines (the most recent ones), all nodes.</summary>
    IReadOnlyList<PipeLiveLine> GetLines(string executionId);

    /// <summary>Asks a running execution to stop. False if it isn't running (anymore).</summary>
    bool Cancel(string executionId);

    /// <summary>
    /// Something changed for the execution with this id (started, node started/finished, output line, finished).
    /// Raised often and on the engine's threads; subscribers should throttle and switch to their own context.
    /// </summary>
    event Action<string>? Changed;
}
