using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Triggers;

namespace gView.Pipe.Plugins.Nodes.Triggers;

[PipeNode(PipeBuiltInNodes.ManualTrigger, "Manual Trigger",
    Category = "Triggers",
    Description = "Starts the workflow when it is run from the editor.")]
public sealed class ManualTriggerNode : IPipeTriggerNode
{
    public Task<IAsyncDisposable?> ActivateAsync(IPipeTriggerContext context, CancellationToken cancellationToken)
        => Task.FromResult<IAsyncDisposable?>(null);

    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
        => Task.FromResult(PipeNodeResult.FromMain(context.GetInput()));
}
