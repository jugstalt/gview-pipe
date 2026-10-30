using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.Nodes.Flow;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Tests.Nodes;

/// <summary>Sort, Limit, Aggregate, Split Out, Remove Duplicates and Wait.</summary>
public class ItemNodeTests
{
    static private PipeExecutionRequest Input(params string[] items)
        => new() { InputItems = items.Select(json => new PipeItem(JsonNode.Parse(json)!.AsObject())).ToList() };

    /// <summary>Runs start → node with the given input and returns the node's output.</summary>
    static private async Task<List<JsonObject>> RunNodeAsync(string type, object? parameters, PipeExecutionRequest request,
        Action<IServiceCollection>? configure = null)
    {
        using var services = PluginTestHost.Create(configure);
        var workflow = new PipeWorkflowBuilder()
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("node", type, parameters)
            .Connect("start", "node")
            .Build();

        var execution = await services.GetRequiredService<IPipeEngine>().ExecuteAsync(workflow, request);
        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        return execution.Output("node");
    }

    static private async Task<string?> RunNodeErrorAsync(string type, object? parameters, PipeExecutionRequest request,
        Action<IServiceCollection>? configure = null)
    {
        using var services = PluginTestHost.Create(configure);
        var workflow = new PipeWorkflowBuilder()
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("node", type, parameters)
            .Connect("start", "node")
            .Build();

        var execution = await services.GetRequiredService<IPipeEngine>().ExecuteAsync(workflow, request);
        Assert.Equal(PipeExecutionStatus.Error, execution.Status);
        return execution.NodeRuns["node"].Error;
    }

    [Fact]
    public async Task Sort_orders_numbers_and_text_with_missing_values_last()
    {
        var output = await RunNodeAsync(PipeBuiltInNodes.Sort, new { fields = new[] { "group", "price desc" } }, Input(
            """{ "id": 1, "group": "b", "price": 5 }""",
            """{ "id": 2, "group": "A", "price": 10 }""",
            """{ "id": 3, "group": "a", "price": 30 }""",
            """{ "id": 4, "price": 99 }""",
            """{ "id": 5, "group": "b", "price": "100" }"""));

        // case-insensitive text, numeric strings compare as numbers, missing group last
        Assert.Equal([3, 2, 5, 1, 4], output.Select(j => j["id"]!.GetValue<int>()));
    }

    [Fact]
    public async Task Sort_without_fields_fails()
        => Assert.Contains("at least one field", await RunNodeErrorAsync(PipeBuiltInNodes.Sort, new { fields = Array.Empty<string>() }, Input("{}")));

    [Theory]
    [InlineData("firstItems", 2, new[] { 1, 2 })]
    [InlineData("lastItems", 2, new[] { 3, 4 })]
    [InlineData("firstItems", 10, new[] { 1, 2, 3, 4 })]
    public async Task Limit_keeps_first_or_last_items(string keep, int maxItems, int[] expected)
    {
        var output = await RunNodeAsync(PipeBuiltInNodes.Limit, new { maxItems, keep },
            Input("""{ "n": 1 }""", """{ "n": 2 }""", """{ "n": 3 }""", """{ "n": 4 }"""));

        Assert.Equal(expected, output.Select(j => j["n"]!.GetValue<int>()));
    }

    [Fact]
    public async Task Aggregate_puts_all_item_data_into_one_array()
    {
        var output = await RunNodeAsync(PipeBuiltInNodes.Aggregate, new { mode = "allItemData", destination = "orders" },
            Input("""{ "n": 1 }""", """{ "n": 2 }"""));

        var item = Assert.Single(output);
        Assert.Equal("""{"orders":[{"n":1},{"n":2}]}""", item.ToJsonString());
    }

    [Fact]
    public async Task Aggregate_collects_individual_fields_and_skips_missing_values()
    {
        var output = await RunNodeAsync(PipeBuiltInNodes.Aggregate, new { mode = "individualFields", fields = new[] { "id", "customer.name" } },
            Input("""{ "id": 1, "customer": { "name": "Ada" } }""", """{ "id": 2 }"""));

        var item = Assert.Single(output);
        Assert.Equal("""{"id":[1,2],"customer":{"name":["Ada"]}}""", item.ToJsonString());
    }

    [Fact]
    public async Task Split_out_turns_array_elements_into_items()
    {
        var output = await RunNodeAsync(PipeBuiltInNodes.SplitOut, new { field = "order.lines" },
            Input("""{ "id": 7, "order": { "lines": [ { "sku": "a" }, { "sku": "b" } ] } }""", """{ "id": 8 }"""));

        Assert.Equal(["""{"sku":"a"}""", """{"sku":"b"}"""], output.Select(j => j.ToJsonString()));
    }

    [Fact]
    public async Task Split_out_can_keep_other_fields_and_names_scalar_values()
    {
        var output = await RunNodeAsync(PipeBuiltInNodes.SplitOut, new { field = "tags", includeOtherFields = true },
            Input("""{ "id": 7, "tags": [ "x", "y" ] }"""));

        Assert.Equal(["""{"id":7,"tags":"x"}""", """{"id":7,"tags":"y"}"""], output.Select(j => j.ToJsonString()));
    }

    [Fact]
    public async Task Remove_duplicates_compares_whole_items_regardless_of_key_order()
    {
        var output = await RunNodeAsync(PipeBuiltInNodes.RemoveDuplicates, new { compare = "allFields" },
            Input("""{ "a": 1, "b": 2 }""", """{ "b": 2, "a": 1 }""", """{ "a": 1, "b": 3 }"""));

        Assert.Equal(["""{"a":1,"b":2}""", """{"a":1,"b":3}"""], output.Select(j => j.ToJsonString()));
    }

    [Fact]
    public async Task Remove_duplicates_compares_selected_fields_and_keeps_the_first()
    {
        var output = await RunNodeAsync(PipeBuiltInNodes.RemoveDuplicates, new { compare = "selectedFields", fields = new[] { "email" } },
            Input("""{ "id": 1, "email": "a@x" }""", """{ "id": 2, "email": "b@x" }""", """{ "id": 3, "email": "a@x" }"""));

        Assert.Equal([1, 2], output.Select(j => j["id"]!.GetValue<int>()));
    }

    [Fact]
    public async Task Wait_pauses_and_passes_items_on()
    {
        var watch = Stopwatch.StartNew();
        var output = await RunNodeAsync(PipeBuiltInNodes.Wait, new { amount = 0.3, unit = "seconds" }, Input("""{ "n": 1 }"""));

        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(250), $"waited only {watch.Elapsed}");
        Assert.Equal(1, Assert.Single(output)["n"]!.GetValue<int>());
    }

    [Fact]
    public async Task Wait_rejects_durations_above_the_configured_maximum()
    {
        var error = await RunNodeErrorAsync(PipeBuiltInNodes.Wait, new { amount = 2, unit = "minutes" }, Input("{}"),
            services => services.Configure<PipeWaitOptions>(o => o.MaxWait = TimeSpan.FromMinutes(1)));

        Assert.Contains("exceeds the allowed maximum", error);
    }
}
