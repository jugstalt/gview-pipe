using gView.Pipe.Abstraction.Nodes;

namespace gView.Pipe.Plugins.Nodes.Utility;

[PipeNode(PipeBuiltInNodes.NoOp, "No Operation",
    Category = "Utility",
    Description = "Passes items through unchanged.")]
public sealed class NoOpNode : IPipeNode
{
    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
        => Task.FromResult(PipeNodeResult.FromMain(context.GetInput()));
}
