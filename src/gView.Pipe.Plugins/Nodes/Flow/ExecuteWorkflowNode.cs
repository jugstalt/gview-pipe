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
    /// <summary>One call with all items.</summary>
    OnceForAllItems,

    /// <summary>One call per item: the sub-workflow is the body of a loop.</summary>
    EachItem,

    /// <summary>One call per batch of <see cref="ExecuteWorkflowNodeParameters.BatchSize"/> items.</summary>
    Batches
}

public sealed class ExecuteWorkflowNodeParameters
{
    [Required, PipeWorkflowReference]
    [PipeParameter(Label = "Workflow", Order = 1, Expressions = false,
        Description = "Needs an Execute Workflow Trigger; it doesn't have to be active")]
    public string WorkflowId { get; set; } = "";

    [PipeParameter(Label = "Call it", Order = 2, Expressions = false)]
    public ExecuteWorkflowMode Mode { get; set; } = ExecuteWorkflowMode.OnceForAllItems;

    [PipeParameter(Label = "Batch size", Order = 3, Expressions = false, Description = "Items per call in batch mode")]
    [Range(1, int.MaxValue)]
    public int BatchSize { get; set; } = 10;
}

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
        foreach (var batch in items.Chunk(batchSize))
        {
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
