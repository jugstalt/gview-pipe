using gView.Pipe.Abstraction.Workflows;

namespace gView.Pipe.Abstraction.Variables;

/// <summary>The environments of the instance and how workflows and executions get theirs.</summary>
public interface IPipeEnvironments
{
    /// <summary>All environments, <see cref="PipeEnvironments.Default"/> first.</summary>
    IReadOnlyList<string> All { get; }

    /// <summary>The environment the instance runs in; set by the administrator.</summary>
    string Instance { get; }

    /// <summary>The first environment set on workflow → group → instance.</summary>
    PipeEffectiveEnvironment Resolve(PipeWorkflow workflow, PipeWorkflowGroup? group);

    /// <inheritdoc cref="Resolve(PipeWorkflow, PipeWorkflowGroup?)"/>
    Task<PipeEffectiveEnvironment> ResolveAsync(PipeWorkflow workflow, CancellationToken cancellationToken = default);

    /// <summary>
    /// The variables of workflow, group and instance for <paramref name="environment"/>: a value for the environment
    /// on any level wins over a <see cref="PipeEnvironments.Default"/> value; within each, the lower level wins.
    /// </summary>
    Task<PipeResolvedVariables> ResolveVariablesAsync(PipeWorkflow workflow, string environment, CancellationToken cancellationToken = default);

    /// <summary>The instance variables: configuration, overridden by the values in <see cref="Storage.IPipeVariableStore"/>.</summary>
    Task<IReadOnlyList<PipeVariable>> GetInstanceVariablesAsync(CancellationToken cancellationToken = default);
}
