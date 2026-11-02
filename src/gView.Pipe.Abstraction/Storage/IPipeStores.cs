using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Variables;
using gView.Pipe.Abstraction.Workflows;

namespace gView.Pipe.Abstraction.Storage;

public interface IPipeWorkflowStore
{
    Task<IReadOnlyList<PipeWorkflow>> ListAsync(CancellationToken cancellationToken = default);

    Task<PipeWorkflow?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Saves the workflow. Assigns an id if it has none.</summary>
    Task SaveAsync(PipeWorkflow workflow, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public interface IPipeWorkflowGroupStore
{
    /// <summary>Ordered by name.</summary>
    Task<IReadOnlyList<PipeWorkflowGroup>> ListAsync(CancellationToken cancellationToken = default);

    Task<PipeWorkflowGroup?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Saves the group. Assigns an id if it has none.</summary>
    /// <exception cref="PipeException">Another group has the same name.</exception>
    Task SaveAsync(PipeWorkflowGroup group, CancellationToken cancellationToken = default);

    /// <summary>Deletes the group only; the workflows that were in it are not touched.</summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public interface IPipeExecutionStore
{
    Task SaveAsync(PipeExecution execution, CancellationToken cancellationToken = default);

    Task<PipeExecution?> GetAsync(string workflowId, string executionId, CancellationToken cancellationToken = default);

    /// <summary>Most recent executions first.</summary>
    Task<IReadOnlyList<PipeExecution>> ListAsync(string workflowId, int maxCount = 50, CancellationToken cancellationToken = default);

    /// <summary>Summaries of the executions of all workflows, most recent first.</summary>
    Task<IReadOnlyList<PipeExecutionSummary>> QueryAsync(PipeExecutionQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all but the <paramref name="keep"/> most recent executions, and those started before <paramref name="olderThan"/>.
    /// </summary>
    Task PruneAsync(string workflowId, int keep, DateTimeOffset? olderThan = null, CancellationToken cancellationToken = default);
}

/// <summary>The variables of the instance level edited in the UI; they override those of the configuration.</summary>
public interface IPipeVariableStore
{
    Task<IReadOnlyList<PipeVariable>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces all variables.</summary>
    Task SaveAsync(IReadOnlyList<PipeVariable> variables, CancellationToken cancellationToken = default);
}

/// <summary>Workflows that are failing right now (ADR 0025). An entry exists from the first failed triggered run until a run succeeds.</summary>
public interface IPipeWorkflowHealthStore
{
    Task<PipeWorkflowHealth?> GetAsync(string workflowId, CancellationToken cancellationToken = default);

    Task SaveAsync(PipeWorkflowHealth health, CancellationToken cancellationToken = default);

    Task DeleteAsync(string workflowId, CancellationToken cancellationToken = default);
}

public interface IPipeCredentialStore
{
    Task<IReadOnlyList<PipeCredentialInfo>> ListAsync(CancellationToken cancellationToken = default);

    Task<PipeCredential?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Saves the credential. Assigns an id if it has none.</summary>
    Task SaveAsync(PipeCredential credential, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
