using gView.Pipe.Abstraction.Data;

namespace gView.Pipe.Abstraction.Execution;

public sealed class PipeExecutionRequest
{
    public PipeExecutionMode Mode { get; init; } = PipeExecutionMode.Manual;

    /// <summary>Node to start at. When null, all nodes without incoming connections are start nodes.</summary>
    public string? StartNodeId { get; init; }

    /// <summary>
    /// "Run to here": execute only this node and the nodes it depends on. Without <see cref="StartNodeId"/>
    /// the start nodes are the roots this node can be reached from.
    /// </summary>
    public string? TargetNodeId { get; init; }

    /// <summary>Items fed into the start node(s). Defaults to one empty item.</summary>
    public IReadOnlyList<PipeItem>? InputItems { get; init; }

    /// <summary>Additional observer for this execution only (e.g. the editor's live view).</summary>
    public IPipeExecutionObserver? Observer { get; init; }

    public string? ExecutionId { get; init; }

    /// <summary>Content for binary references of <see cref="InputItems"/>, moved into the execution's binary store.</summary>
    public IReadOnlyDictionary<string, byte[]>? BinaryContent { get; init; }
}
