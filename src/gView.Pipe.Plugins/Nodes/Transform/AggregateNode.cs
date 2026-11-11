using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Transform;

public enum AggregateMode
{
    [PipeEnumValue("One list with all items, e.g. to send them in one request", Label = "All items into one list")]
    AllItemData,

    [PipeEnumValue("One list per field with the values of all items, e.g. all email addresses", Label = "Values of single fields")]
    IndividualFields
}

public sealed class AggregateNodeParameters
{
    [PipeParameter(Label = "Aggregate", Expressions = false, Order = 1, Description = "What to collect from the items")]
    public AggregateMode Mode { get; set; } = AggregateMode.AllItemData;

    [PipeParameter(Label = "Put into field", Expressions = false, Order = 2, ShowWhen = nameof(Mode), ShowWhenValues = ["allItemData"],
        Description = "Name of the field that gets the list, e.g. orders")]
    public string Destination { get; set; } = "data";

    [PipeParameter(Label = "Fields", Expressions = false, Order = 3, Placeholder = "customer.name", ShowWhen = nameof(Mode), ShowWhenValues = ["individualFields"],
        Description = "Fields to collect, e.g. email or customer.name; items without the field are left out")]
    public List<string> Fields { get; set; } = new();
}

[PipeNode(PipeBuiltInNodes.Aggregate, "Aggregate",
    Category = "Transform",
    Description = "Combines all items into a single item with lists. Attachments are not carried over.")]
[PipeNodeExamples]
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
