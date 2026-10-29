using gView.Pipe.Abstraction.Data;
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
    string NodeId { get; }
    string NodeName { get; }

    TParameters GetParameters<TParameters>() where TParameters : class, new();

    IPipeTriggerHost Host { get; }

    ILogger Logger { get; }

    /// <summary>Starts an execution of the workflow at this trigger node.</summary>
    Task FireAsync(IReadOnlyList<PipeItem> items, CancellationToken cancellationToken);
}
