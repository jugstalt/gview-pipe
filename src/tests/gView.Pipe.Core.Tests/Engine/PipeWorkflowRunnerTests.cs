using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Core.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Tests.Engine;

public class PipeWorkflowRunnerTests
{
    [Fact]
    public async Task Runs_beyond_max_concurrent_runs_are_skipped()
    {
        using var services = PipeTestHost.Create();
        var runner = services.GetRequiredService<IPipeWorkflowRunner>();
        var workflow = new PipeWorkflowBuilder()
            .Node("slow", TestNodeIds.Slow, new { delayMs = 300 })
            .Build();

        var first = runner.RunAsync(workflow);
        var second = await runner.RunAsync(workflow);

        Assert.Null(second);
        Assert.Equal(PipeExecutionStatus.Success, (await first)!.Status);
    }

    [Fact]
    public async Task Executions_are_stored_without_data_when_configured()
    {
        using var services = PipeTestHost.Create();
        var runner = services.GetRequiredService<IPipeWorkflowRunner>();
        var executions = services.GetRequiredService<IPipeExecutionStore>();
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, new { items = new[] { new { secret = "x" } } })
            .Configure(w => w.Settings.SaveExecutionData = false)
            .Build();

        var execution = await runner.RunAsync(workflow);
        var stored = await executions.GetAsync(workflow.Id, execution!.Id);

        Assert.NotNull(execution.NodeRuns["emit"].Outputs);
        Assert.False(stored!.DataSaved);
        Assert.Null(stored.NodeRuns["emit"].Outputs);
        Assert.Equal(1, stored.NodeRuns["emit"].OutputCounts["main"]);
    }

    [Fact]
    public async Task Active_workflow_triggers_are_armed_on_startup_and_start_executions()
    {
        using var services = PipeTestHost.Create();
        var workflow = new PipeWorkflowBuilder()
            .Node("trigger", TestNodeIds.Trigger)
            .Node("echo", TestNodeIds.Echo)
            .Connect("trigger", "echo")
            .Configure(w => w.Active = true)
            .Build();
        await services.GetRequiredService<IPipeWorkflowStore>().SaveAsync(workflow);

        foreach (var hostedService in services.GetServices<IHostedService>())
        {
            await hostedService.StartAsync(CancellationToken.None);
        }

        Assert.True(services.GetRequiredService<IPipeTriggerManager>().IsActive(workflow.Id));

        var context = TestTriggerNode.Activated[workflow.Id];
        await context.FireAsync([new PipeItem(new JsonObject { ["fired"] = true })], CancellationToken.None);

        var execution = Assert.Single(await services.GetRequiredService<IPipeExecutionStore>().ListAsync(workflow.Id));
        Assert.Equal(PipeExecutionMode.Trigger, execution.Mode);
        Assert.True(execution.NodeRuns["echo"].Outputs!["main"][0].Json["fired"]!.GetValue<bool>());
    }
}
