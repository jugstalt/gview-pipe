using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Transform;

public sealed class SortNodeParameters
{
    [PipeParameter(Label = "Sort by", Expressions = false, Placeholder = "price desc",
        Description = "Field paths in order of priority; append ' desc' for descending")]
    public List<string> Fields { get; set; } = new();
}

[PipeNode(PipeBuiltInNodes.Sort, "Sort",
    Category = "Transform",
    Description = "Sorts the items by one or more fields.")]
public sealed class SortNode : IPipeNode<SortNodeParameters>
{
    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var keys = context.GetParameters<SortNodeParameters>().Fields
            .Select(ParseKey)
            .OfType<(string Path, bool Descending)>()
            .ToList();
        if (keys.Count == 0)
        {
            throw new InvalidOperationException("Sort needs at least one field");
        }

        var items = context.GetInput().ToList();
        // stable: items with equal keys keep their order
        var sorted = items.Order(Comparer<PipeItem>.Create((a, b) =>
        {
            foreach (var (path, descending) in keys)
            {
                var result = PipeJsonComparer.Compare(PipeJsonPath.Get(a.Json, path), PipeJsonPath.Get(b.Json, path), descending);
                if (result != 0)
                {
                    return result;
                }
            }
            return 0;
        }));

        return Task.FromResult(PipeNodeResult.FromMain(sorted.ToList()));
    }

    static private (string Path, bool Descending)? ParseKey(string text)
    {
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => null,
            1 => (parts[0], false),
            _ => (parts[0], parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase))
        };
    }
}

/// <summary>Orders JSON values: numbers numerically, text culture-invariant and case-insensitive, missing values last.</summary>
static public class PipeJsonComparer
{
    static public int Compare(JsonNode? left, JsonNode? right, bool descending = false)
    {
        var leftMissing = IsMissing(left);
        var rightMissing = IsMissing(right);
        if (leftMissing || rightMissing)
        {
            // missing values go last in both directions
            return leftMissing == rightMissing ? 0 : leftMissing ? 1 : -1;
        }

        var result = PipeConditions.TryGetNumber(left, out var a) && PipeConditions.TryGetNumber(right, out var b)
            ? a.CompareTo(b)
            : string.Compare(PipeConditions.ToText(left), PipeConditions.ToText(right), CultureInfo.InvariantCulture, CompareOptions.IgnoreCase);

        return descending ? -result : result;
    }

    static private bool IsMissing(JsonNode? value) => value is null || value is JsonValue v && v.GetValueKind() == JsonValueKind.Null;
}
