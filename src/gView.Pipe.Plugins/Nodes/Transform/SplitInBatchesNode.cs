using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Transform;

public sealed class SplitInBatchesNodeParameters
{
    [PipeParameter(Label = "Batch size", Expressions = false, Order = 1, Description = "Items per batch, e.g. 100; the last batch may have fewer")]
    [Range(1, int.MaxValue)]
    public int BatchSize { get; set; } = 10;

    [PipeParameter(Label = "Put into field", Expressions = false, Order = 2, Description = "Name of the field that holds the items of a batch, e.g. items")]
    public string Destination { get; set; } = "items";
}

[PipeNode(PipeBuiltInNodes.SplitInBatches, "Split in Batches",
    Category = "Transform",
    Description = "Turns the items into one item per batch: batch (from 1), batches, count and the items as a list. "
                  + "Attachments are not carried over.")]
[PipeNodeExamples]
public sealed class SplitInBatchesNode : IPipeNode<SplitInBatchesNodeParameters>
{
    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var parameters = context.GetParameters<SplitInBatchesNodeParameters>();
        var batchSize = Math.Max(1, parameters.BatchSize);
        var destination = string.IsNullOrWhiteSpace(parameters.Destination) ? "items" : parameters.Destination.Trim();

        var batches = context.GetInput().Chunk(batchSize).ToList();
        var output = new List<PipeItem>(batches.Count);
        for (var i = 0; i < batches.Count; i++)
        {
            var json = new JsonObject
            {
                ["batch"] = i + 1,
                ["batches"] = batches.Count,
                ["count"] = batches[i].Length
            };
            PipeJsonPath.Set(json, destination, new JsonArray(batches[i].Select(item => (JsonNode)item.Json.DeepClone()).ToArray()));
            output.Add(new PipeItem(json));
        }

        return Task.FromResult(PipeNodeResult.FromMain(output));
    }
}
