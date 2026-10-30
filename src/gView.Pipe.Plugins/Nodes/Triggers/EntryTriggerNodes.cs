using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Triggers;

namespace gView.Pipe.Plugins.Nodes.Triggers;

[PipeNode(PipeBuiltInNodes.ExecuteWorkflowTrigger, "Execute Workflow Trigger",
    Category = "Triggers",
    Description = "Starts the workflow when another workflow calls it with Execute Workflow; outputs the caller's items.")]
public sealed class ExecuteWorkflowTriggerNode : IPipeSubWorkflowTrigger
{
    public Task<IAsyncDisposable?> ActivateAsync(IPipeTriggerContext context, CancellationToken cancellationToken)
        => Task.FromResult<IAsyncDisposable?>(null);

    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
        => Task.FromResult(PipeNodeResult.FromMain(context.GetInput()));
}

[PipeNode(PipeBuiltInNodes.ErrorTrigger, "Error Trigger",
    Category = "Triggers",
    Description = "Starts the workflow when a triggered run of a workflow that names it as error workflow fails. "
                  + "Outputs { workflow, execution, node } of the failed run.")]
public sealed class ErrorTriggerNode : IPipeErrorTrigger
{
    public Task<IAsyncDisposable?> ActivateAsync(IPipeTriggerContext context, CancellationToken cancellationToken)
        => Task.FromResult<IAsyncDisposable?>(null);

    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
        => Task.FromResult(PipeNodeResult.FromMain(context.GetInput()));
}
