using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Execution;
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

    /// <summary>Deletes all but the <paramref name="keep"/> most recent executions.</summary>
    Task PruneAsync(string workflowId, int keep, CancellationToken cancellationToken = default);
}

public interface IPipeCredentialStore
{
    Task<IReadOnlyList<PipeCredentialInfo>> ListAsync(CancellationToken cancellationToken = default);

    Task<PipeCredential?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Saves the credential. Assigns an id if it has none.</summary>
    Task SaveAsync(PipeCredential credential, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
