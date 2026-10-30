using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Transform;

public sealed class SplitOutNodeParameters
{
    [PipeParameter(Label = "Field to split out", Expressions = false, Placeholder = "orders", Order = 1,
        Description = "Path of an array field; every element becomes an item")]
    [Required]
    public string Field { get; set; } = "";

    [PipeParameter(Label = "Keep the other fields of the item", Expressions = false, Order = 2)]
    public bool IncludeOtherFields { get; set; }
}

[PipeNode(PipeBuiltInNodes.SplitOut, "Split Out",
    Category = "Transform",
    Description = "Turns an array field into one item per element.")]
public sealed class SplitOutNode : IPipeNode<SplitOutNodeParameters>
{
    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput();
        var output = new List<PipeItem>();

        for (var i = 0; i < items.Count; i++)
        {
            output.AddRange(Split(items[i], context.GetParameters<SplitOutNodeParameters>(items[i], i)));
        }

        return Task.FromResult(PipeNodeResult.FromMain(output));
    }

    static private IEnumerable<PipeItem> Split(PipeItem item, SplitOutNodeParameters parameters)
    {
        var path = parameters.Field.Trim();
        if (path.Length == 0)
        {
            throw new InvalidOperationException("Split Out needs a field");
        }

        var value = PipeJsonPath.Get(item.Json, path);
        // a single value is treated like an array with one element; a missing field yields nothing
        var elements = value switch
        {
            null => [],
            JsonArray array => array.ToList(),
            _ => [value]
        };

        JsonObject? rest = null;
        if (parameters.IncludeOtherFields)
        {
            rest = (JsonObject)item.Json.DeepClone();
            PipeJsonPath.TryRemove(rest, path, out _);
        }

        var name = path.Split('.')[^1];
        foreach (var element in elements)
        {
            var json = rest is null ? new JsonObject() : (JsonObject)rest.DeepClone();
            if (element is JsonObject obj)
            {
                foreach (var (key, child) in obj)
                {
                    json[key] = child?.DeepClone();
                }
            }
            else
            {
                // scalars keep the field's name
                json[name] = element?.DeepClone();
            }

            yield return new PipeItem(json) { Binary = item.Binary };
        }
    }
}
