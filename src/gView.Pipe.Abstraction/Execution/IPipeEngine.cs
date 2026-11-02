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

    /// <summary>
    /// Runs a stored execution again with the current workflow, in its environment (mode <see cref="PipeExecutionMode.Replay"/>,
    /// see ADR 0015). Waits for a free slot instead of being skipped.
    /// </summary>
    /// <param name="fromNodeId">Node to execute again with everything downstream; null to replay from the start.</param>
    /// <exception cref="PipeException">The execution or workflow doesn't exist, or the execution was stored without data.</exception>
    Task<PipeExecution> ReplayAsync(string workflowId, string executionId, string? fromNodeId = null, IPipeExecutionObserver? observer = null, CancellationToken cancellationToken = default);
}
