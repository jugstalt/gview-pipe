namespace gView.Pipe.Abstraction.Workflows;

/// <summary>Where the effective error workflow of a workflow is set (see ADR 0017).</summary>
public enum PipeErrorWorkflowSource
{
    /// <summary>Set nowhere, or switched off with <see cref="PipeErrorWorkflows.None"/>.</summary>
    None,
    Instance,
    Group,
    Workflow
}

/// <param name="WorkflowId">The error workflow to start, or null for none.</param>
/// <param name="SourceName">Name of the group it comes from.</param>
public sealed record PipeEffectiveErrorWorkflow(string? WorkflowId, PipeErrorWorkflowSource Source, string? SourceName = null);

static public class PipeErrorWorkflows
{
    /// <summary>Value of <see cref="PipeWorkflowSettings.ErrorWorkflowId"/> that stops the inheritance; never a valid id.</summary>
    public const string None = "!none";

    /// <summary>Parameter of the Error Trigger: minutes between two runs for the same failed workflow (0 = no limit).</summary>
    public const string ThrottleMinutesParameter = "throttleMinutes";

    public const int DefaultThrottleMinutes = 15;
}

/// <summary>Resolves the error workflow of a workflow: workflow → group → instance.</summary>
public interface IPipeErrorWorkflows
{
    /// <summary>The instance's error workflow (<c>Pipe:ErrorWorkflowId</c>), or null.</summary>
    string? Instance { get; }

    PipeEffectiveErrorWorkflow Resolve(PipeWorkflow workflow, PipeWorkflowGroup? group);
}
