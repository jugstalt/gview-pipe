using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Transform;

public sealed class SetNodeParameters
{
    [PipeParameter(Label = "Fields", Description = "Field (e.g. address.city) → value, e.g. Vienna or {{ $json.price * 1.2 }}", Order = 1)]
    public Dictionary<string, JsonNode?> Fields { get; set; } = new();

    [PipeParameter(Label = "Rename", Description = "Old field → new name, e.g. mail → email; done before the fields are set", Order = 2)]
    public Dictionary<string, string> Rename { get; set; } = new();

    [PipeParameter(Label = "Keep only set fields", Order = 3, Expressions = false,
        Description = "Drop all other fields: the items have only the fields set above")]
    public bool KeepOnlySet { get; set; }
}

[PipeNode(PipeBuiltInNodes.Set, "Set",
    Category = "Transform",
    Description = "Sets, renames or keeps fields of each item.")]
[PipeNodeExamples]
public sealed class SetNode : PipeItemNodeBase<SetNodeParameters>
{
    protected override Task<PipeItem?> ProcessItemAsync(PipeItem item, SetNodeParameters parameters, IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var json = parameters.KeepOnlySet ? new JsonObject() : (JsonObject)item.Json.DeepClone();

        if (!parameters.KeepOnlySet)
        {
            foreach (var (from, to) in parameters.Rename)
            {
                if (PipeJsonPath.TryRemove(json, from, out var value))
                {
                    PipeJsonPath.Set(json, to, value);
                }
            }
        }

        foreach (var (path, value) in parameters.Fields)
        {
            PipeJsonPath.Set(json, path, value?.DeepClone());
        }

        return Task.FromResult<PipeItem?>(new PipeItem(json) { Binary = item.Binary });
    }
}
