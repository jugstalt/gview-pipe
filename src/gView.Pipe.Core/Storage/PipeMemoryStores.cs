using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Serialization;
using System.Collections.Concurrent;

namespace gView.Pipe.Core.Storage;

// Default stores when no persistent store is configured (headless use, tests).
// Objects are cloned on the way in and out, so callers never share state with the store.

internal sealed class PipeMemoryWorkflowStore : IPipeWorkflowStore
{
    private readonly ConcurrentDictionary<string, PipeWorkflow> _workflows = new();

    public Task<IReadOnlyList<PipeWorkflow>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PipeWorkflow>>(_workflows.Values.Select(PipeJson.Clone).OrderBy(w => w.Name).ToList());

    public Task<PipeWorkflow?> GetAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(_workflows.TryGetValue(id, out var workflow) ? PipeJson.Clone(workflow) : null);

    public Task SaveAsync(PipeWorkflow workflow, CancellationToken cancellationToken = default)
    {
        PipeStoreIds.Prepare(workflow);
        _workflows[workflow.Id] = PipeJson.Clone(workflow);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        _workflows.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}

internal sealed class PipeMemoryExecutionStore : IPipeExecutionStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PipeExecution>> _executions = new();

    public Task SaveAsync(PipeExecution execution, CancellationToken cancellationToken = default)
    {
        _executions.GetOrAdd(execution.WorkflowId, _ => new())[execution.Id] = PipeJson.Clone(execution);
        return Task.CompletedTask;
    }

    public Task<PipeExecution?> GetAsync(string workflowId, string executionId, CancellationToken cancellationToken = default)
        => Task.FromResult(_executions.TryGetValue(workflowId, out var executions) && executions.TryGetValue(executionId, out var execution)
            ? PipeJson.Clone(execution)
            : null);

    public Task<IReadOnlyList<PipeExecution>> ListAsync(string workflowId, int maxCount = 50, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PipeExecution>>(_executions.TryGetValue(workflowId, out var executions)
            ? executions.Values.OrderByDescending(e => e.StartedAt).Take(maxCount).Select(PipeJson.Clone).ToList()
            : []);

    public Task PruneAsync(string workflowId, int keep, CancellationToken cancellationToken = default)
    {
        if (_executions.TryGetValue(workflowId, out var executions))
        {
            foreach (var execution in executions.Values.OrderByDescending(e => e.StartedAt).Skip(keep))
            {
                executions.TryRemove(execution.Id, out _);
            }
        }
        return Task.CompletedTask;
    }
}

internal sealed class PipeMemoryCredentialStore : IPipeCredentialStore
{
    private readonly ConcurrentDictionary<string, PipeCredential> _credentials = new();

    public Task<IReadOnlyList<PipeCredentialInfo>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PipeCredentialInfo>>(_credentials.Values
            .Select(c => new PipeCredentialInfo(c.Id, c.Name, c.TypeId))
            .OrderBy(c => c.Name)
            .ToList());

    public Task<PipeCredential?> GetAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(_credentials.TryGetValue(id, out var credential) ? PipeJson.Clone(credential) : null);

    public Task SaveAsync(PipeCredential credential, CancellationToken cancellationToken = default)
    {
        PipeStoreIds.Prepare(credential);
        _credentials[credential.Id] = PipeJson.Clone(credential);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        _credentials.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
