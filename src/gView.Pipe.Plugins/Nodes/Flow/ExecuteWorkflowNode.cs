using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace gView.Pipe.Plugins.Nodes.Flow;

public enum ExecuteWorkflowMode
{
    [PipeEnumValue("One call with all items")]
    OnceForAllItems,

    [PipeEnumValue("One call per item: the other workflow is the body of a loop")]
    EachItem,

    [PipeEnumValue("One call per batch of items, e.g. 100 at a time", Label = "In batches")]
    Batches
}

public sealed class ExecuteWorkflowNodeParameters
{
    [Required, PipeWorkflowReference]
    [PipeParameter(Label = "Workflow", Order = 1, Expressions = false,
        Description = "The workflow to run; it starts with an Execute Workflow Trigger and doesn't have to be active")]
    public string WorkflowId { get; set; } = "";

    [PipeParameter(Label = "Call it", Order = 2, Expressions = false, Description = "How often the other workflow runs")]
    public ExecuteWorkflowMode Mode { get; set; } = ExecuteWorkflowMode.OnceForAllItems;

    [PipeParameter(Label = "Batch size", Order = 3, Expressions = false, ShowWhen = nameof(Mode), ShowWhenValues = ["batches"],
        Description = "Items per call, e.g. 100")]
    [Range(1, int.MaxValue)]
    public int BatchSize { get; set; } = 10;

    [PipeParameter(Label = "Pause between calls (ms)", Order = 4, Expressions = false, ShowWhen = nameof(Mode), ShowWhenValues = ["eachItem", "batches"],
        Description = "Waits this long before each call but the first, e.g. 1000 for the rate limit of an API; 0 = no pause")]
    [Range(0, 600000)]
    public int DelayMs { get; set; }
}

[PipeNodeExamples]
[PipeNode(PipeBuiltInNodes.ExecuteWorkflow, "Execute Workflow",
    Category = "Flow",
    Description = "Runs another workflow with the items and outputs what its last node returned. "
                  + "Call it per item or per batch to loop.")]
public sealed class ExecuteWorkflowNode : IPipeNode<ExecuteWorkflowNodeParameters>
{
    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var parameters = context.GetParameters<ExecuteWorkflowNodeParameters>();
        if (string.IsNullOrWhiteSpace(parameters.WorkflowId))
        {
            throw new PipeException("Select the workflow to execute");
        }

        var items = context.GetInput();
        var batchSize = parameters.Mode switch
        {
            ExecuteWorkflowMode.EachItem => 1,
            ExecuteWorkflowMode.Batches => Math.Max(1, parameters.BatchSize),
            _ => Math.Max(1, items.Count)
        };

        var output = new List<PipeItem>();
        var delay = TimeSpan.FromMilliseconds(Math.Clamp(parameters.DelayMs, 0, 600000));
        var first = true;
        foreach (var batch in items.Chunk(batchSize))
        {
            if (!first && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
            first = false;

            // calls run one after the other, like the iterations of a loop
            var execution = await context.ExecuteWorkflowAsync(parameters.WorkflowId, batch, cancellationToken);
            if (execution.Status != PipeExecutionStatus.Success)
            {
                throw new PipeException($"Workflow '{execution.WorkflowName}' {execution.Status.ToString().ToLowerInvariant()}: {execution.Error} (execution {execution.Id})");
            }

            context.Logger.LogDebug("Execution {ExecutionId} of {WorkflowName} returned its items", execution.Id, execution.WorkflowName);
            output.AddRange(PipeExecutionOutput.LastNodeItems(execution));
        }

        return PipeNodeResult.FromMain(output);
    }
}
