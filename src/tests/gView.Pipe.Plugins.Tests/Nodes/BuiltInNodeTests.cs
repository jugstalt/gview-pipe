using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.Nodes.Flow;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Tests.Nodes;

public class BuiltInNodeTests
{
    static private PipeExecutionRequest Input(params string[] items)
        => new() { InputItems = items.Select(json => new PipeItem(JsonNode.Parse(json)!.AsObject())).ToList() };

    static private async Task<PipeExecution> RunAsync(PipeWorkflowBuilder builder, PipeExecutionRequest request)
    {
        using var services = PluginTestHost.Create();
        var execution = await services.GetRequiredService<IPipeEngine>().ExecuteAsync(builder.Build(), request);
        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        return execution;
    }

    [Fact]
    public async Task Set_sets_renames_and_keeps_fields()
    {
        var builder = new PipeWorkflowBuilder()
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("set", PipeBuiltInNodes.Set, new
            {
                fields = new Dictionary<string, object>
                {
                    ["address.city"] = "Graz",
                    ["label"] = "{{ $json.first }} {{ $json.last }}",
                    ["copy"] = "{{ $json.age }}"
                },
                rename = new Dictionary<string, string> { ["first"] = "firstName" }
            })
            .Node("keep", PipeBuiltInNodes.Set, new
            {
                fields = new Dictionary<string, object> { ["only"] = "{{ $json.label }}" },
                keepOnlySet = true
            })
            .Connect("start", "set")
            .Connect("set", "keep");

        var execution = await RunAsync(builder, Input("""{ "first": "Ada", "last": "Lovelace", "age": 36 }"""));

        var set = Assert.Single(execution.Output("set"));
        Assert.Equal("Ada", set["firstName"]!.GetValue<string>());
        Assert.Null(set["first"]);
        Assert.Equal("Graz", set["address"]!["city"]!.GetValue<string>());
        // the label is evaluated against the input item, before the rename
        Assert.Equal("Ada Lovelace", set["label"]!.GetValue<string>());
        Assert.Equal(36, set["copy"]!.GetValue<int>());

        var keep = Assert.Single(execution.Output("keep"));
        Assert.Equal("""{"only":"Ada Lovelace"}""", keep.ToJsonString());
    }

    [Fact]
    public async Task If_routes_items_by_condition()
    {
        var builder = new PipeWorkflowBuilder()
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("if", PipeBuiltInNodes.If, new { value1 = "{{ $json.n }}", operation = "greaterThan", value2 = 10 })
            .Connect("start", "if");

        var execution = await RunAsync(builder, Input("""{ "n": 5 }""", """{ "n": 50 }""", """{ "n": "100" }"""));

        Assert.Equal([50, 100], execution.Output("if", IfNode.TruePort).Select(j => int.Parse(j["n"]!.ToString())));
        Assert.Equal(5, Assert.Single(execution.Output("if", IfNode.FalsePort))["n"]!.GetValue<int>());
    }

    [Fact]
    public async Task Filter_keeps_matching_items()
    {
        var builder = new PipeWorkflowBuilder()
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("filter", PipeBuiltInNodes.Filter, new { value1 = "{{ $json.tags }}", operation = "contains", value2 = "gis" })
            .Connect("start", "filter");

        var execution = await RunAsync(builder, Input("""{ "id": 1, "tags": ["gis", "web"] }""", """{ "id": 2, "tags": ["web"] }"""));

        Assert.Equal(1, Assert.Single(execution.Output("filter"))["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task Switch_routes_to_case_or_fallback_and_reports_dynamic_ports()
    {
        var parameters = new { value = "{{ $json.kind }}", cases = new[] { "point", "line" } };
        var builder = new PipeWorkflowBuilder()
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("switch", PipeBuiltInNodes.Switch, parameters)
            .Connect("start", "switch");

        var execution = await RunAsync(builder, Input("""{ "kind": "line" }""", """{ "kind": "polygon" }""", """{ "kind": "point" }"""));

        Assert.Equal("point", Assert.Single(execution.Output("switch", SwitchNode.CasePort(0)))["kind"]!.GetValue<string>());
        Assert.Equal("line", Assert.Single(execution.Output("switch", SwitchNode.CasePort(1)))["kind"]!.GetValue<string>());
        Assert.Equal("polygon", Assert.Single(execution.Output("switch", SwitchNode.FallbackPort))["kind"]!.GetValue<string>());

        using var services = PluginTestHost.Create();
        var registry = services.GetRequiredService<IPipeNodeRegistry>();
        var ports = registry.GetOutputs(registry.Find(Guid.Parse(PipeBuiltInNodes.Switch))!, builder.Build().Nodes[1].Parameters);
        Assert.Equal(["case-0", "case-1", "fallback"], ports.Select(p => p.Name));
    }

    [Theory]
    [InlineData("append", """[{"a":1},{"a":2},{"b":3}]""")]
    [InlineData("combineByIndex", """[{"a":1,"b":3},{"a":2}]""")]
    public async Task Merge_combines_both_inputs(string mode, string expected)
    {
        var builder = new PipeWorkflowBuilder()
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("left", PipeBuiltInNodes.Set, new { fields = new { a = "{{ $json.a }}" }, keepOnlySet = true })
            .Node("only-b", PipeBuiltInNodes.Filter, new { value1 = "{{ $json.b }}", operation = "isNotEmpty" })
            .Node("only-a", PipeBuiltInNodes.Filter, new { value1 = "{{ $json.a }}", operation = "isNotEmpty" })
            .Node("merge", PipeBuiltInNodes.Merge, new { mode })
            .Connect("start", "only-a")
            .Connect("only-a", "left")
            .Connect("start", "only-b")
            .Connect("left", "merge", targetPort: MergeNode.Input1)
            .Connect("only-b", "merge", targetPort: MergeNode.Input2);

        var execution = await RunAsync(builder, Input("""{ "a": 1 }""", """{ "a": 2 }""", """{ "b": 3 }"""));

        Assert.Equal(expected, new JsonArray(execution.Output("merge").Select(j => (JsonNode)j.DeepClone()).ToArray()).ToJsonString());
    }

    [Fact]
    public async Task Log_and_noop_pass_items_through()
    {
        var builder = new PipeWorkflowBuilder()
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("log", PipeBuiltInNodes.Log, new { message = "value {{ $json.v }}" })
            .Node("noop", PipeBuiltInNodes.NoOp)
            .Connect("start", "log")
            .Connect("log", "noop");

        var execution = await RunAsync(builder, Input("""{ "v": 1 }""", """{ "v": 2 }"""));

        Assert.Equal(2, execution.Output("noop").Count);
    }
}
