namespace gView.Pipe.Abstraction.Workflows;

/// <summary>The open failure of a workflow: kept until a triggered run succeeds again (ADR 0025).</summary>
public sealed class PipeWorkflowHealth
{
    public string WorkflowId { get; set; } = "";

    /// <summary>Start of the first failed run of this streak.</summary>
    public DateTimeOffset FailingSince { get; set; }

    public string FailedExecutionId { get; set; } = "";

    /// <summary>Failed runs in this streak, reported or not.</summary>
    public int Failures { get; set; }

    /// <summary>The error workflow ran for at least one failure of this streak; only then a recovery is reported.</summary>
    public bool Notified { get; set; }
}
