using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Abstraction.Workflows;
using System.ComponentModel.DataAnnotations;

namespace gView.Pipe.Plugins.Nodes.Triggers;

[PipeNodeExamples]
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

public sealed class ErrorTriggerNodeParameters
{
    /// <summary>Read by the runner before the error workflow starts (<see cref="PipeErrorWorkflows.ThrottleMinutesParameter"/>).</summary>
    [Range(0, 1440), PipeParameter(Label = "Throttle minutes", Expressions = false,
        Description = "Per failed workflow, start at most once in this time; failures in between are counted in 'suppressed' of the next run. 0 reports every failure.")]
    public int ThrottleMinutes { get; set; } = PipeErrorWorkflows.DefaultThrottleMinutes;
}

[PipeNodeExamples]
[PipeNode(PipeBuiltInNodes.RecoveryTrigger, "Recovery Trigger",
    Category = "Triggers",
    Description = "Starts the workflow when a workflow that failed and was reported to this error workflow runs successfully again (triggered runs only). "
                  + "Outputs { workflow, execution, failedSince, failedExecution, failures }; execution.url and failedExecution.url link to the runs when Pipe:PublicUrl is set.")]
public sealed class RecoveryTriggerNode : IPipeRecoveryTrigger
{
    public Task<IAsyncDisposable?> ActivateAsync(IPipeTriggerContext context, CancellationToken cancellationToken)
        => Task.FromResult<IAsyncDisposable?>(null);

    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
        => Task.FromResult(PipeNodeResult.FromMain(context.GetInput()));
}

[PipeNodeExamples]
[PipeNode(PipeBuiltInNodes.ErrorTrigger, "Error Trigger",
    Category = "Triggers",
    Description = "Starts the workflow when a triggered run of a workflow that uses it as error workflow fails (set on the workflow, its group or the instance). "
                  + "Outputs { workflow, execution, node, subWorkflow, suppressed } of the failed run; execution.url links to it when Pipe:PublicUrl is set.")]
public sealed class ErrorTriggerNode : IPipeErrorTrigger, IPipeNode<ErrorTriggerNodeParameters>
{
    public Task<IAsyncDisposable?> ActivateAsync(IPipeTriggerContext context, CancellationToken cancellationToken)
        => Task.FromResult<IAsyncDisposable?>(null);

    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
        => Task.FromResult(PipeNodeResult.FromMain(context.GetInput()));
}
