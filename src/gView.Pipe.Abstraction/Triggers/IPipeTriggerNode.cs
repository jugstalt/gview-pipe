using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using Microsoft.Extensions.Logging;

namespace gView.Pipe.Abstraction.Triggers;

/// <summary>
/// A node that starts executions. Triggers have no inputs; when an execution starts at a trigger,
/// the trigger's <see cref="IPipeNode.ExecuteAsync"/> receives the trigger items on its main input.
/// </summary>
public interface IPipeTriggerNode : IPipeNode
{
    /// <summary>
    /// Arms the trigger for an active workflow (e.g. registers a schedule).
    /// Returns a handle that disarms it, or <c>null</c> for triggers that only fire manually.
    /// </summary>
    Task<IAsyncDisposable?> ActivateAsync(IPipeTriggerContext context, CancellationToken cancellationToken);
}

public interface IPipeTriggerContext
{
    string WorkflowId { get; }
    string WorkflowName { get; }
    string NodeId { get; }
    string NodeName { get; }

    TParameters GetParameters<TParameters>() where TParameters : class, new();

    Task<PipeCredential> GetCredentialAsync(string credentialId, CancellationToken cancellationToken = default);

    IPipeTriggerHost Host { get; }

    ILogger Logger { get; }

    /// <summary>
    /// Starts an execution of the workflow at this trigger node and completes when it has finished.
    /// Returns null if the run was skipped because of the workflow's concurrency limit.
    /// </summary>
    Task<PipeExecution?> FireAsync(IReadOnlyList<PipeItem> items, CancellationToken cancellationToken, PipeTriggerFireOptions? options = null);
}

public sealed class PipeTriggerFireOptions
{
    /// <summary>Id for the execution, e.g. to report it before the execution has finished.</summary>
    public string? ExecutionId { get; init; }

    /// <summary>
    /// Content for the binary references of the fired items (key: <see cref="PipeBinaryRef.Id"/>).
    /// The engine moves it into the execution's binary store.
    /// </summary>
    public IReadOnlyDictionary<string, byte[]>? BinaryContent { get; init; }
}
