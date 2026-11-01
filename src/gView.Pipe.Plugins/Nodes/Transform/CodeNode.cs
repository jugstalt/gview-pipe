using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Expressions;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Serialization;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Transform;

public enum CodeNodeMode
{
    /// <summary>Runs once; <c>items</c> holds all input JSON objects, returns an array of objects.</summary>
    AllItems,

    /// <summary>Runs per item with <c>$json</c>; returns an object, or null to drop the item.</summary>
    EachItem
}

public sealed class CodeNodeParameters
{
    public const string DefaultCode = """
        // items: array with the JSON of all input items
        // return an array of objects; console.log writes to the log
        return items.map(item => ({ ...item, processed: true }));
        """;

    [PipeParameter(Label = "Mode", Order = 1, Expressions = false)]
    public CodeNodeMode Mode { get; set; } = CodeNodeMode.AllItems;

    [PipeParameter(Label = "JavaScript", Order = 2, Code = true, CodeLanguage = "javascript",
        Description = "Available: items (all items mode), $json, $itemIndex (each item mode), $input, $node, $workflow, $execution, $now")]
    public string Code { get; set; } = DefaultCode;
}

[PipeNode(PipeBuiltInNodes.Code, "Code",
    Category = "Transform",
    Description = "Transforms items with JavaScript, once for all items or once per item.")]
public sealed class CodeNode : IPipeNode<CodeNodeParameters>
{
    private readonly IPipeScriptRunner _scripts;

    public CodeNode(IPipeScriptRunner scripts)
    {
        _scripts = scripts;
    }

    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        // read raw: the code must not go through {{ }} evaluation
        var parameters = context.RawParameters.Deserialize<CodeNodeParameters>(PipeJson.Options) ?? new CodeNodeParameters();
        var items = context.GetInput();
        Action<string> log = message => context.Logger.LogInformation("[{Workflow}/{Node}] {Message}", context.WorkflowName, context.NodeName, message);

        var output = parameters.Mode == CodeNodeMode.EachItem
            ? RunEachItem(context, parameters.Code, items, log, cancellationToken)
            : RunAllItems(context, parameters.Code, items, log, cancellationToken);

        return Task.FromResult(PipeNodeResult.FromMain(output));
    }

    private List<PipeItem> RunAllItems(IPipeNodeContext context, string code, IReadOnlyList<PipeItem> items, Action<string> log, CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, JsonNode?>
        {
            ["items"] = new JsonArray(items.Select(i => (JsonNode)i.Json.DeepClone()).ToArray())
        };

        var result = _scripts.Run(code, context.CreateExpressionScope(), variables, log, cancellationToken);
        var objects = result switch
        {
            null => [],
            JsonArray array => array.Select((node, index) => node as JsonObject
                ?? throw new PipeException($"Code returned an array whose element {index} is not an object")).ToList(),
            JsonObject single => [single],
            _ => throw new PipeException("Code must return an array of objects")
        };

        // with an unchanged item count, binary attachments stay with their item
        var keepBinary = objects.Count == items.Count;
        return objects.Select((json, index) => new PipeItem((JsonObject)json.DeepClone())
        {
            Binary = keepBinary ? items[index].Binary : new()
        }).ToList();
    }

    private List<PipeItem> RunEachItem(IPipeNodeContext context, string code, IReadOnlyList<PipeItem> items, Action<string> log, CancellationToken cancellationToken)
    {
        var output = new List<PipeItem>(items.Count);

        for (var i = 0; i < items.Count; i++)
        {
            var result = _scripts.Run(code, context.CreateExpressionScope(items[i], i), log: log, cancellationToken: cancellationToken);
            switch (result)
            {
                case null:
                    break;
                case JsonObject json:
                    output.Add(new PipeItem((JsonObject)json.DeepClone()) { Binary = items[i].Binary });
                    break;
                default:
                    throw new PipeException($"Code must return an object or null (item {i})");
            }
        }

        return output;
    }
}
