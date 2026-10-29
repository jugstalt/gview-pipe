using gView.Pipe.Abstraction.Workflows;

namespace gView.Pipe.Abstraction.Execution;

/// <summary>Executes a workflow graph. Does not persist anything; see <see cref="IPipeWorkflowRunner"/>.</summary>
public interface IPipeEngine
{
    Task<PipeExecution> ExecuteAsync(PipeWorkflow workflow, PipeExecutionRequest? request = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs workflows the way the application does: honors <see cref="PipeWorkflowSettings.MaxConcurrentRuns"/>
/// and stores the execution history.
/// </summary>
public interface IPipeWorkflowRunner
{
    /// <returns>The execution, or <c>null</c> if the run was skipped because of the concurrency limit.</returns>
    Task<PipeExecution?> RunAsync(string workflowId, PipeExecutionRequest? request = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="RunAsync(string, PipeExecutionRequest?, CancellationToken)"/>
    Task<PipeExecution?> RunAsync(PipeWorkflow workflow, PipeExecutionRequest? request = null, CancellationToken cancellationToken = default);
}
