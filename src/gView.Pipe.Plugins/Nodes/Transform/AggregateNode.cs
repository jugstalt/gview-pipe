using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Transform;

public enum AggregateMode
{
    /// <summary>One array with the JSON of every item.</summary>
    AllItemData,

    /// <summary>One array per field with that field's values of all items.</summary>
    IndividualFields
}

public sealed class AggregateNodeParameters
{
    [PipeParameter(Label = "Aggregate", Expressions = false, Order = 1)]
    public AggregateMode Mode { get; set; } = AggregateMode.AllItemData;

    [PipeParameter(Label = "Put into field", Expressions = false, Order = 2,
        Description = "All item data: name of the array field")]
    public string Destination { get; set; } = "data";

    [PipeParameter(Label = "Fields", Expressions = false, Order = 3, Placeholder = "customer.name",
        Description = "Individual fields: paths to collect; items without the field are left out")]
    public List<string> Fields { get; set; } = new();
}

[PipeNode(PipeBuiltInNodes.Aggregate, "Aggregate",
    Category = "Transform",
    Description = "Combines all items into a single item with arrays. Binary data is not carried over.")]
public sealed class AggregateNode : IPipeNode<AggregateNodeParameters>
{
    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var parameters = context.GetParameters<AggregateNodeParameters>();
        var items = context.GetInput();
        var json = new JsonObject();

        if (parameters.Mode == AggregateMode.IndividualFields)
        {
            if (parameters.Fields.Count == 0)
            {
                throw new InvalidOperationException("Aggregate needs at least one field");
            }

            foreach (var path in parameters.Fields.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()))
            {
                var values = items
                    .Select(i => PipeJsonPath.Get(i.Json, path))
                    .Where(v => v is not null)
                    .Select(v => v!.DeepClone())
                    .ToArray();
                PipeJsonPath.Set(json, path, new JsonArray(values));
            }
        }
        else
        {
            var destination = string.IsNullOrWhiteSpace(parameters.Destination) ? "data" : parameters.Destination.Trim();
            PipeJsonPath.Set(json, destination, new JsonArray(items.Select(i => (JsonNode)i.Json.DeepClone()).ToArray()));
        }

        return Task.FromResult(PipeNodeResult.FromMain([new PipeItem(json)]));
    }
}
