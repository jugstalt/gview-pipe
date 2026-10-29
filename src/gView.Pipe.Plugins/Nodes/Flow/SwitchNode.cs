using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Flow;

public sealed class SwitchNodeParameters
{
    [PipeParameter(Label = "Value", Description = "Value to compare, usually an expression", Order = 1)]
    public JsonNode? Value { get; set; }

    [PipeParameter(Label = "Cases", Description = "One output per case; items matching no case go to 'fallback'", Order = 2)]
    public List<JsonNode?> Cases { get; set; } = new();
}

[PipeNode(PipeBuiltInNodes.Switch, "Switch",
    Category = "Flow",
    Description = "Routes each item to the output of the first matching case.")]
public sealed class SwitchNode : IPipeNode<SwitchNodeParameters>, IPipeDynamicPorts
{
    public const string FallbackPort = "fallback";

    static public string CasePort(int index) => $"case-{index}";

    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput();
        var outputs = new Dictionary<string, List<PipeItem>>();

        for (var i = 0; i < items.Count; i++)
        {
            var parameters = context.GetParameters<SwitchNodeParameters>(items[i], i);
            var caseIndex = parameters.Cases.FindIndex(c => PipeConditions.Evaluate(parameters.Value, PipeConditionOperation.IsEqual, c));
            var port = caseIndex >= 0 ? CasePort(caseIndex) : FallbackPort;

            if (!outputs.TryGetValue(port, out var portItems))
            {
                outputs[port] = portItems = new List<PipeItem>();
            }
            portItems.Add(items[i]);
        }

        return Task.FromResult(new PipeNodeResult(outputs.ToDictionary(p => p.Key, p => (IReadOnlyList<PipeItem>)p.Value)));
    }

    public IReadOnlyList<PipePort> GetInputs(JsonObject parameters) => [new PipePort(PipePorts.Main)];

    public IReadOnlyList<PipePort> GetOutputs(JsonObject parameters)
    {
        var cases = parameters.FirstOrDefault(p => p.Key.Equals("cases", StringComparison.OrdinalIgnoreCase)).Value as JsonArray;
        var ports = Enumerable.Range(0, cases?.Count ?? 0)
            .Select(i => new PipePort(CasePort(i), PipeConditions.ToText(cases![i])))
            .ToList();
        ports.Add(new PipePort(FallbackPort, "Fallback"));
        return ports;
    }
}
