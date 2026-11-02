using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Variables;
using gView.Pipe.Abstraction.Workflows;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Razor.Editor;

/// <summary>A completion for expressions: the full path, its JSON type and a short sample value.</summary>
public sealed record PipeCompletion(string Path, string Type, string? Sample = null, string? Detail = null);

/// <summary>What the browser gets for the selected node (see ADR 0019).</summary>
public sealed record PipeCompletionSet(IReadOnlyList<PipeCompletion> Items, string? Hint);

/// <summary>Builds the completions for the expressions of one node from the shown run, pin data and variables.</summary>
static public class PipeCompletionCatalog
{
    public const int MaxEntries = 500, MaxDepth = 4, MaxItems = 20, MaxSample = 40;

    static private readonly PipeCompletion[] Globals =
    [
        new("$json", "object", Detail: "the current item"),
        new("$input", "object", Detail: "all input items"),
        new("$input.all()", "array"),
        new("$input.first()", "object"),
        new("$node", "object", Detail: "output of an earlier node: $node[\"Name\"].json"),
        new("$vars", "object", Detail: "variables of the environment"),
        new("$now", "string", Detail: "current time, ISO 8601"),
        new("$itemIndex", "number", Detail: "index of the current item"),
        new("$environment", "string"),
        new("$workflow", "object"),
        new("$workflow.id", "string"),
        new("$workflow.name", "string"),
        new("$execution", "object"),
        new("$execution.id", "string")
    ];

    /// <param name="execution">The run shown in the editor, if any.</param>
    /// <param name="variables">Resolved variables of the workflow's environment.</param>
    static public PipeCompletionSet Build(PipeWorkflow workflow, string nodeId, PipeExecution? execution, PipeResolvedVariables? variables)
    {
        var result = new List<PipeCompletion>(Globals);
        var seen = new HashSet<string>(result.Select(c => c.Path), StringComparer.Ordinal);

        void Add(PipeCompletion completion)
        {
            if (result.Count < MaxEntries && seen.Add(completion.Path))
            {
                result.Add(completion);
            }
        }

        foreach (var (name, value) in (variables?.Values ?? new Dictionary<string, string>()).OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            var secret = variables!.Secrets.Contains(name);
            Add(new PipeCompletion($"$vars.{name}", "string", secret ? null : Shorten(value), secret ? "secret" : null));
        }

        var input = InputOf(workflow, nodeId, execution);
        foreach (var completion in Paths("$json", input))
        {
            Add(completion);
        }

        foreach (var upstream in Upstream(workflow, nodeId))
        {
            if (OutputOf(workflow, upstream, execution) is not { } items)
            {
                continue;
            }
            var prefix = $"$node[\"{upstream.Name.Replace("\"", "\\\"")}\"]";
            Add(new PipeCompletion(prefix, "object", Detail: "earlier node"));
            Add(new PipeCompletion(prefix + ".all", "array", Detail: $"{items.Count} item(s)"));
            foreach (var completion in Paths(prefix + ".json", items))
            {
                Add(completion);
            }
        }

        var hint = input.Count == 0
            ? "Run the workflow (or Run to here on the node before) to get field suggestions"
            : null;
        return new PipeCompletionSet(result, hint);
    }

    /// <summary>The node's input in the shown run; otherwise the outputs (run or pin data) of its direct predecessors.</summary>
    static private IReadOnlyList<JsonObject> InputOf(PipeWorkflow workflow, string nodeId, PipeExecution? execution)
    {
        if (execution?.NodeRuns.GetValueOrDefault(nodeId)?.Inputs?.GetValueOrDefault(PipePorts.Main) is { Count: > 0 } inputs)
        {
            return inputs.Select(i => i.Json).ToList();
        }
        return workflow.Connections
            .Where(c => c.TargetNodeId == nodeId)
            .Select(c => workflow.Nodes.FirstOrDefault(n => n.Id == c.SourceNodeId))
            .OfType<PipeWorkflowNode>()
            .SelectMany(n => OutputOf(workflow, n, execution) ?? [])
            .ToList();
    }

    static private IReadOnlyList<JsonObject>? OutputOf(PipeWorkflow workflow, PipeWorkflowNode node, PipeExecution? execution)
    {
        if (execution?.NodeRuns.GetValueOrDefault(node.Id)?.Outputs?.GetValueOrDefault(PipePorts.Main) is { Count: > 0 } outputs)
        {
            return outputs.Select(i => i.Json).ToList();
        }
        return workflow.PinData?.GetValueOrDefault(node.Id)?.GetValueOrDefault(PipePorts.Main) is { Count: > 0 } pinned
            ? pinned.Select(i => i.Json).ToList()
            : null;
    }

    /// <summary>Every node the given one can be reached from, nearest first.</summary>
    static private IEnumerable<PipeWorkflowNode> Upstream(PipeWorkflow workflow, string nodeId)
    {
        var seen = new HashSet<string> { nodeId };
        var queue = new Queue<string>([nodeId]);
        while (queue.Count > 0)
        {
            foreach (var source in workflow.Connections.Where(c => c.TargetNodeId == queue.Peek()).Select(c => c.SourceNodeId))
            {
                if (seen.Add(source) && workflow.Nodes.FirstOrDefault(n => n.Id == source) is { } node)
                {
                    queue.Enqueue(source);
                    yield return node;
                }
            }
            queue.Dequeue();
        }
    }

    /// <summary>The fields of the first items, merged; arrays are described by their first element as <c>[0]</c>.</summary>
    static public IEnumerable<PipeCompletion> Paths(string root, IReadOnlyList<JsonObject> items)
    {
        var found = new Dictionary<string, PipeCompletion>(StringComparer.Ordinal);
        foreach (var item in items.Take(MaxItems))
        {
            Walk(root, item, 1, found);
        }
        return found.Values;
    }

    static private void Walk(string path, JsonNode? node, int depth, Dictionary<string, PipeCompletion> found)
    {
        if (depth > MaxDepth || found.Count >= MaxEntries)
        {
            return;
        }
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (found.Count >= MaxEntries)
                    {
                        return;
                    }
                    var child = IsIdentifier(key) ? $"{path}.{key}" : $"{path}[{JsonSerializer.Serialize(key)}]";
                    found.TryAdd(child, new PipeCompletion(child, TypeOf(value), Sample(value)));
                    Walk(child, value, depth + 1, found);
                }
                break;
            case JsonArray { Count: > 0 } array:
                var first = $"{path}[0]";
                found.TryAdd(first, new PipeCompletion(first, TypeOf(array[0]), Sample(array[0])));
                Walk(first, array[0], depth + 1, found);
                break;
        }
    }

    static private bool IsIdentifier(string key)
        => key.Length > 0 && (char.IsLetter(key[0]) || key[0] is '_' or '$') && key.All(c => char.IsLetterOrDigit(c) || c is '_' or '$');

    static private string TypeOf(JsonNode? value) => value switch
    {
        null => "null",
        JsonObject => "object",
        JsonArray => "array",
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            _ => "null"
        },
        _ => "null"
    };

    static private string? Sample(JsonNode? value) => value switch
    {
        null => "null",
        JsonObject obj => $"{{ {obj.Count} field(s) }}",
        JsonArray array => $"[ {array.Count} ]",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => Shorten(v.GetValue<string>()),
        _ => Shorten(value.ToJsonString())
    };

    static private string Shorten(string text)
        => text.Length > MaxSample ? text[..(MaxSample - 1)] + "…" : text;
}
