using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Workflows;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Tests.Nodes;

/// <summary>Execute Workflow, Execute Workflow Trigger, error workflows and the Error Trigger.</summary>
public sealed class SubWorkflowTests : IDisposable
{
    private readonly ServiceProvider _services = PluginTestHost.Create();

    public void Dispose() => _services.Dispose();

    private IPipeWorkflowRunner Runner => _services.GetRequiredService<IPipeWorkflowRunner>();

    private async Task<PipeWorkflow> SaveAsync(PipeWorkflow workflow)
    {
        await _services.GetRequiredService<IPipeWorkflowStore>().SaveAsync(workflow);
        return workflow;
    }

    private Task<IReadOnlyList<PipeExecution>> ExecutionsAsync(string workflowId)
        => _services.GetRequiredService<IPipeExecutionStore>().ListAsync(workflowId);

    static private IReadOnlyList<PipeItem> Numbers(int count)
        => Enumerable.Range(1, count).Select(n => new PipeItem(new JsonObject { ["n"] = n })).ToList();

    /// <summary>Sub-workflow: doubles n and records how many items the call got.</summary>
    static private PipeWorkflow Doubler() => new PipeWorkflowBuilder("Doubler", "doubler")
        .Node("in", PipeBuiltInNodes.ExecuteWorkflowTrigger)
        .Node("double", PipeBuiltInNodes.Set, new { fields = new { doubled = "{{ $json.n * 2 }}", callSize = "{{ $input.all().length }}" } })
        .Connect("in", "double")
        .Build();

    static private PipeWorkflow Caller(object parameters, string id = "caller") => new PipeWorkflowBuilder("Caller", id)
        .Node("start", PipeBuiltInNodes.ManualTrigger)
        .Node("call", PipeBuiltInNodes.ExecuteWorkflow, parameters)
        .Connect("start", "call")
        .Build();

    [Theory]
    [InlineData("onceForAllItems", 1, new[] { 5, 5, 5, 5, 5 })]
    [InlineData("eachItem", 5, new[] { 1, 1, 1, 1, 1 })]
    [InlineData("batches", 3, new[] { 2, 2, 2, 2, 1 })]
    public async Task Execute_workflow_calls_the_sub_workflow_per_mode(string mode, int expectedCalls, int[] expectedCallSizes)
    {
        await SaveAsync(Doubler());
        var caller = Caller(new { workflowId = "doubler", mode, batchSize = 2 });

        var execution = await Runner.RunAsync(caller, new PipeExecutionRequest { InputItems = Numbers(5) });

        Assert.Equal(PipeExecutionStatus.Success, execution!.Status);
        var output = execution.Output("call");
        Assert.Equal([2, 4, 6, 8, 10], output.Select(j => j["doubled"]!.GetValue<int>()));
        Assert.Equal(expectedCallSizes, output.Select(j => j["callSize"]!.GetValue<int>()));

        var calls = await ExecutionsAsync("doubler");
        Assert.Equal(expectedCalls, calls.Count);
        Assert.All(calls, call =>
        {
            Assert.Equal(PipeExecutionMode.SubWorkflow, call.Mode);
            Assert.Equal(execution.Id, call.ParentExecutionId);
            Assert.Equal("caller", call.ParentWorkflowId);
            Assert.Equal(1, call.Depth);
        });
    }

    [Fact]
    public async Task Sub_workflow_reads_and_returns_binaries_of_the_caller()
    {
        await SaveAsync(new PipeWorkflowBuilder("Passthrough", "passthrough")
            .Node("in", PipeBuiltInNodes.ExecuteWorkflowTrigger)
            .Node("noop", PipeBuiltInNodes.NoOp)
            .Connect("in", "noop")
            .Build());
        var caller = new PipeWorkflowBuilder("Caller", "caller")
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("call", PipeBuiltInNodes.ExecuteWorkflow, new { workflowId = "passthrough" })
            .Node("code", PipeBuiltInNodes.Code, new { mode = "eachItem", code = "return $json;" })
            .Connect("start", "call")
            .Connect("call", "code")
            .Build();

        var item = new PipeItem(new JsonObject()) { Binary = { ["file"] = new PipeBinaryRef("bin-1", "a.txt", "text/plain", 3) } };
        var execution = await Runner.RunAsync(caller, new PipeExecutionRequest
        {
            InputItems = [item],
            BinaryContent = new Dictionary<string, byte[]> { ["bin-1"] = "abc"u8.ToArray() }
        });

        Assert.Equal(PipeExecutionStatus.Success, execution!.Status);
        var returned = Assert.Single(execution.NodeRuns["call"].Outputs!["main"]);
        Assert.Equal("bin-1", returned.Binary["file"].Id);
    }

    [Fact]
    public async Task A_workflow_calling_itself_stops_at_the_nesting_limit()
    {
        var recursive = await SaveAsync(new PipeWorkflowBuilder("Recursive", "recursive")
            .Node("in", PipeBuiltInNodes.ExecuteWorkflowTrigger)
            .Node("call", PipeBuiltInNodes.ExecuteWorkflow, new { workflowId = "recursive" })
            .Connect("in", "call")
            .Configure(w => w.Settings.MaxConcurrentRuns = 20)
            .Build());

        var execution = await Runner.RunAsync(recursive);

        Assert.Equal(PipeExecutionStatus.Error, execution!.Status);
        Assert.Contains("nested more than 10 levels", execution.Error);
    }

    [Fact]
    public async Task Failing_sub_workflow_fails_the_calling_node()
    {
        await SaveAsync(new PipeWorkflowBuilder("Broken", "broken")
            .Node("in", PipeBuiltInNodes.ExecuteWorkflowTrigger)
            .Node("code", PipeBuiltInNodes.Code, new { code = "throw new Error('boom');" })
            .Connect("in", "code")
            .Build());

        var execution = await Runner.RunAsync(Caller(new { workflowId = "broken" }));

        Assert.Equal(PipeExecutionStatus.Error, execution!.Status);
        Assert.Contains("Workflow 'Broken' error", execution.NodeRuns["call"].Error);
        Assert.Contains("boom", execution.NodeRuns["call"].Error);
    }

    [Fact]
    public async Task Missing_entry_trigger_is_reported()
    {
        await SaveAsync(new PipeWorkflowBuilder("Plain", "plain").Node("start", PipeBuiltInNodes.ManualTrigger).Build());

        var execution = await Runner.RunAsync(Caller(new { workflowId = "plain" }));

        Assert.Contains("has no Execute Workflow Trigger", execution!.NodeRuns["call"].Error);
    }

    static private PipeWorkflow Failing(string errorWorkflowId) => new PipeWorkflowBuilder("Nightly", "nightly")
        .Node("start", PipeBuiltInNodes.ManualTrigger)
        .Node("code", PipeBuiltInNodes.Code, new { code = "throw new Error('database down');" })
        .Connect("start", "code")
        .Configure(w => w.Settings.ErrorWorkflowId = errorWorkflowId)
        .Build();

    [Fact]
    public async Task Failed_triggered_run_starts_the_error_workflow()
    {
        await SaveAsync(new PipeWorkflowBuilder("Alerts", "alerts")
            .Node("error", PipeBuiltInNodes.ErrorTrigger)
            .Node("noop", PipeBuiltInNodes.NoOp)
            .Connect("error", "noop")
            .Build());

        var failed = await Runner.RunAsync(Failing("alerts"), new PipeExecutionRequest { Mode = PipeExecutionMode.Trigger });

        var alert = Assert.Single(await ExecutionsAsync("alerts"));
        Assert.Equal(PipeExecutionMode.Error, alert.Mode);
        Assert.Equal(failed!.Id, alert.ParentExecutionId);

        var item = Assert.Single(alert.NodeRuns["noop"].Outputs!["main"]).Json;
        Assert.Equal("Nightly", item["workflow"]!["name"]!.GetValue<string>());
        Assert.Equal(failed.Id, item["execution"]!["id"]!.GetValue<string>());
        Assert.Equal("trigger", item["execution"]!["mode"]!.GetValue<string>());
        Assert.Equal("code", item["node"]!["name"]!.GetValue<string>());
        Assert.Contains("database down", item["node"]!["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Manual_runs_and_missing_error_workflows_do_not_break_anything()
    {
        await SaveAsync(new PipeWorkflowBuilder("Alerts", "alerts").Node("error", PipeBuiltInNodes.ErrorTrigger).Build());

        var manual = await Runner.RunAsync(Failing("alerts"));
        var missing = await Runner.RunAsync(Failing("does-not-exist"), new PipeExecutionRequest { Mode = PipeExecutionMode.Trigger });

        Assert.Equal(PipeExecutionStatus.Error, manual!.Status);
        Assert.Equal(PipeExecutionStatus.Error, missing!.Status);
        Assert.Empty(await ExecutionsAsync("alerts"));
    }
}
