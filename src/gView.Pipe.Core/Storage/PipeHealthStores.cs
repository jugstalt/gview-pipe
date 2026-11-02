using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Options;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace gView.Pipe.Core.Storage;

internal sealed class PipeMemoryWorkflowHealthStore : IPipeWorkflowHealthStore
{
    private readonly ConcurrentDictionary<string, PipeWorkflowHealth> _health = new();

    public Task<PipeWorkflowHealth?> GetAsync(string workflowId, CancellationToken cancellationToken = default)
        => Task.FromResult(_health.TryGetValue(workflowId, out var health) ? PipeJson.Clone(health) : null);

    public Task SaveAsync(PipeWorkflowHealth health, CancellationToken cancellationToken = default)
    {
        _health[health.WorkflowId] = PipeJson.Clone(health);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        _health.TryRemove(workflowId, out _);
        return Task.CompletedTask;
    }
}

/// <summary>One file per failing workflow: <c>{root}/health/{id}.json</c>. Deleting the workflow deletes it too.</summary>
internal sealed class PipeFileWorkflowHealthStore : IPipeWorkflowHealthStore
{
    private readonly string _folder;

    public PipeFileWorkflowHealthStore(IOptions<PipeFileStoreOptions> options)
    {
        _folder = FolderOf(options.Value);
    }

    static public string FolderOf(PipeFileStoreOptions options) => Path.Combine(Path.GetFullPath(options.RootPath), "health");

    public async Task<PipeWorkflowHealth?> GetAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        var file = PathOf(workflowId);
        return File.Exists(file) ? PipeJson.Deserialize<PipeWorkflowHealth>(await File.ReadAllTextAsync(file, cancellationToken)) : null;
    }

    public Task SaveAsync(PipeWorkflowHealth health, CancellationToken cancellationToken = default)
        => PipeFiles.WriteAtomicAsync(PathOf(health.WorkflowId), PipeJson.Serialize(health), cancellationToken);

    public Task DeleteAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        DeleteFile(_folder, workflowId);
        return Task.CompletedTask;
    }

    /// <summary>Deletes the file of a workflow; nothing happens if there is none (or no folder yet).</summary>
    static public void DeleteFile(string folder, string workflowId)
    {
        var file = Path.Combine(folder, $"{PipeStoreIds.Validate(workflowId)}.json");
        if (File.Exists(file))
        {
            File.Delete(file);
        }
    }

    private string PathOf(string workflowId) => Path.Combine(_folder, $"{PipeStoreIds.Validate(workflowId)}.json");
}
