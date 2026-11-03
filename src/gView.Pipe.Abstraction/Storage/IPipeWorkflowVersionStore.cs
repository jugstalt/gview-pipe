using gView.Pipe.Abstraction.Workflows;

namespace gView.Pipe.Abstraction.Storage;

/// <summary>An earlier saved state of a workflow (ADR 0031).</summary>
public sealed class PipeWorkflowVersionInfo
{
    /// <summary>The time stamp of the save, <c>yyyyMMddHHmmssfff</c>.</summary>
    public string Id { get; set; } = "";

    public DateTimeOffset SavedAt { get; set; }

    /// <summary>What changed against the version before (<see cref="PipeWorkflowChanges"/>); "first version" for the oldest.</summary>
    public string Summary { get; set; } = "";
}

public interface IPipeWorkflowVersionStore
{
    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<PipeWorkflowVersionInfo>> ListAsync(string workflowId, CancellationToken cancellationToken = default);

    Task<PipeWorkflow?> GetAsync(string workflowId, string versionId, CancellationToken cancellationToken = default);
}
