using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Transform;

public enum RemoveDuplicatesCompare
{
    AllFields,
    SelectedFields
}

public sealed class RemoveDuplicatesNodeParameters
{
    [PipeParameter(Label = "Compare", Expressions = false, Order = 1)]
    public RemoveDuplicatesCompare Compare { get; set; } = RemoveDuplicatesCompare.AllFields;

    [PipeParameter(Label = "Fields", Expressions = false, Order = 2, Placeholder = "email",
        Description = "Selected fields: paths that together identify an item")]
    public List<string> Fields { get; set; } = new();
}

[PipeNode(PipeBuiltInNodes.RemoveDuplicates, "Remove Duplicates",
    Category = "Transform",
    Description = "Keeps the first of several items with equal JSON (or equal selected fields).")]
public sealed class RemoveDuplicatesNode : IPipeNode<RemoveDuplicatesNodeParameters>
{
    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var parameters = context.GetParameters<RemoveDuplicatesNodeParameters>();
        var fields = parameters.Fields.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToList();
        if (parameters.Compare == RemoveDuplicatesCompare.SelectedFields && fields.Count == 0)
        {
            throw new InvalidOperationException("Remove Duplicates needs at least one field to compare");
        }

        var seen = new HashSet<string>();
        var kept = context.GetInput()
            .Where(item => seen.Add(Key(item, parameters.Compare, fields)))
            .ToList();

        return Task.FromResult(PipeNodeResult.FromMain(kept));
    }

    /// <summary>Canonical JSON (properties sorted), so key order doesn't make items different.</summary>
    static private string Key(PipeItem item, RemoveDuplicatesCompare compare, List<string> fields)
        => compare == RemoveDuplicatesCompare.SelectedFields
            ? new JsonArray(fields.Select(f => Canonical(PipeJsonPath.Get(item.Json, f))).ToArray()).ToJsonString()
            : Canonical(item.Json)!.ToJsonString();

    static private JsonNode? Canonical(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Canonical(p.Value)))),
        JsonArray array => new JsonArray(array.Select(Canonical).ToArray()),
        _ => node?.DeepClone()
    };
}
