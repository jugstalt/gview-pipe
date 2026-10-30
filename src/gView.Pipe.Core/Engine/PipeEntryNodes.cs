using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Workflows;

namespace gView.Pipe.Core.Engine;

/// <summary>Finds the node where a sub-workflow or error workflow starts, by the marker interface of its type.</summary>
static internal class PipeEntryNodes
{
    /// <returns>The first enabled node whose type implements <typeparamref name="TMarker"/>, or null.</returns>
    static public PipeWorkflowNode? Find<TMarker>(PipeWorkflow workflow, IPipeNodeRegistry registry)
        => workflow.Nodes.FirstOrDefault(node => !node.Disabled
            && registry.Find(node.Type)?.NodeType is { } type
            && typeof(TMarker).IsAssignableFrom(type));
}
