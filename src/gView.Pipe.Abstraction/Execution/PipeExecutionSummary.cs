namespace gView.Pipe.Abstraction.Execution;

/// <summary>An execution without its node runs, for listing the history of all workflows.</summary>
public sealed class PipeExecutionSummary
{
    public string Id { get; set; } = "";
    public string WorkflowId { get; set; } = "";
    public string WorkflowName { get; set; } = "";
    public PipeExecutionMode Mode { get; set; }
    public string Environment { get; set; } = gView.Pipe.Abstraction.Variables.PipeEnvironments.Default;
    public PipeExecutionStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? Error { get; set; }
    public string? ErrorNodeId { get; set; }

    /// <summary>Name of <see cref="ErrorNodeId"/> at the time of the execution.</summary>
    public string? ErrorNodeName { get; set; }

    public string? ParentExecutionId { get; set; }
    public string? ReplayOfExecutionId { get; set; }
    public string? StartedBy { get; set; }
    public bool DataSaved { get; set; } = true;

    static public PipeExecutionSummary From(PipeExecution execution) => new()
    {
        Id = execution.Id,
        WorkflowId = execution.WorkflowId,
        WorkflowName = execution.WorkflowName,
        Mode = execution.Mode,
        Environment = execution.Environment,
        Status = execution.Status,
        StartedAt = execution.StartedAt,
        FinishedAt = execution.FinishedAt,
        Error = execution.Error,
        ErrorNodeId = execution.ErrorNodeId,
        ErrorNodeName = execution.ErrorNodeId is { } nodeId ? execution.NodeRuns.GetValueOrDefault(nodeId)?.NodeName : null,
        ParentExecutionId = execution.ParentExecutionId,
        ReplayOfExecutionId = execution.ReplayOfExecutionId,
        StartedBy = execution.StartedBy,
        DataSaved = execution.DataSaved
    };

    /// <summary>True when the summary passes all filters of <paramref name="query"/>.</summary>
    public bool Matches(PipeExecutionQuery query)
        => (query.WorkflowIds is not { Count: > 0 } || query.WorkflowIds.Contains(WorkflowId))
           && (query.Statuses is not { Count: > 0 } || query.Statuses.Contains(Status))
           && (query.Modes is not { Count: > 0 } || query.Modes.Contains(Mode))
           && (query.From is null || StartedAt >= query.From)
           && (query.Before is null || StartedAt < query.Before)
           && (query.StartedBy is null || string.Equals(StartedBy, query.StartedBy, StringComparison.Ordinal));
}

/// <summary>Filters for <see cref="Storage.IPipeExecutionStore.QueryAsync"/>. Empty filters match everything.</summary>
public sealed record PipeExecutionQuery
{
    public IReadOnlyCollection<string>? WorkflowIds { get; init; }
    public IReadOnlyCollection<PipeExecutionStatus>? Statuses { get; init; }
    public IReadOnlyCollection<PipeExecutionMode>? Modes { get; init; }

    /// <summary>Started at or after.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Started before; pass the start of the last row to load the next page.</summary>
    public DateTimeOffset? Before { get; init; }

    /// <summary>Only executions started by exactly this (e.g. <c>portal:anna</c>).</summary>
    public string? StartedBy { get; init; }

    public int MaxCount { get; init; } = 50;
}
