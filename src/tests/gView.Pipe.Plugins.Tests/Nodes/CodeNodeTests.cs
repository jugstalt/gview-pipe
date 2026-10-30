using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.Nodes.Transform;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Tests.Nodes;

public class CodeNodeTests
{
    static private async Task<PipeExecution> RunAsync(object codeParameters, params string[] inputs)
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("code", PipeBuiltInNodes.Code, codeParameters)
            .Connect("start", "code")
            .Build();

        using var services = PluginTestHost.Create();
        return await services.GetRequiredService<IPipeEngine>().ExecuteAsync(workflow, new PipeExecutionRequest
        {
            InputItems = inputs.Select(json => new PipeItem(JsonNode.Parse(json)!.AsObject())).ToList()
        });
    }

    [Fact]
    public async Task All_items_mode_maps_the_item_array()
    {
        var execution = await RunAsync(
            new { mode = "allItems", code = "return items.filter(i => i.n > 1).map(i => ({ n: i.n, square: i.n * i.n }));" },
            """{ "n": 1 }""", """{ "n": 2 }""", """{ "n": 3 }""");

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        Assert.Equal([4, 9], execution.Output("code").Select(j => j["square"]!.GetValue<int>()));
    }

    [Fact]
    public async Task Each_item_mode_returns_an_object_or_null_per_item()
    {
        var execution = await RunAsync(
            new { mode = "eachItem", code = "if ($json.skip) return null;\nreturn { ...$json, index: $itemIndex };" },
            """{ "a": 1 }""", """{ "skip": true }""", """{ "a": 3 }""");

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        Assert.Equal("""[{"a":1,"index":0},{"a":3,"index":2}]""",
            new JsonArray(execution.Output("code").Select(j => (JsonNode)j.DeepClone()).ToArray()).ToJsonString());
    }

    [Fact]
    public async Task Code_is_not_treated_as_expression_template()
    {
        var execution = await RunAsync(
            new { code = "const text = '{{ not an expression }}';\nreturn [{ text }];" },
            """{ }""");

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        Assert.Equal("{{ not an expression }}", execution.Output("code")[0]["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Console_log_ends_up_in_the_node_run_log()
    {
        var execution = await RunAsync(new { code = "console.log('count', items.length, items[0]);\nreturn items;" }, """{ "a": 1 }""");

        var entry = Assert.Single(execution.NodeRuns["code"].Logs);
        Assert.Contains("""count 1 {"a":1}""", entry.Message);
    }

    [Fact]
    public async Task Invalid_results_and_script_errors_fail_the_node()
    {
        var wrongResult = await RunAsync(new { code = "return 42;" }, """{ }""");
        var scriptError = await RunAsync(new { code = "const x = 1;\nundefinedFunction();" }, """{ }""");

        Assert.Equal(PipeExecutionStatus.Error, wrongResult.Status);
        Assert.Contains("array of objects", wrongResult.Error);
        Assert.Equal(PipeExecutionStatus.Error, scriptError.Status);
        Assert.Contains("line 2", scriptError.Error);
    }

    [Fact]
    public void Default_code_is_a_working_example()
    {
        Assert.Contains("return items.map", new CodeNodeParameters().Code);
    }
}
