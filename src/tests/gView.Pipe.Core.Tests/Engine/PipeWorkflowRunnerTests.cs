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
        Assert.Null(stored.NodeRuns["emit"].Inputs);
        Assert.Equal(1, stored.NodeRuns["emit"].InputCounts["main"]);
        Assert.Equal(1, stored.NodeRuns["emit"].OutputCounts["main"]);
    }

    [Fact]
    public async Task Triggered_runs_wait_in_the_queue_in_order_and_are_skipped_when_it_is_full()
    {
        using var services = PipeTestHost.Create();
        var runner = services.GetRequiredService<IPipeWorkflowRunner>();
        var running = services.GetRequiredService<IPipeRunningExecutions>();
        var workflow = new PipeWorkflowBuilder()
            .Node("slow", TestNodeIds.Slow, new { delayMs = 300 })
            .Configure(w => w.Settings.MaxQueuedRuns = 2)
            .Build();
        PipeExecutionRequest Triggered(string id, PipeQueueTicket ticket) => new() { Mode = PipeExecutionMode.Trigger, ExecutionId = id, QueueTicket = ticket };

        var tickets = Enumerable.Range(0, 4).Select(_ => new PipeQueueTicket()).ToArray();
        var first = runner.RunAsync(workflow, Triggered("r0", tickets[0]));
        var second = runner.RunAsync(workflow, Triggered("r1", tickets[1]));
        var third = runner.RunAsync(workflow, Triggered("r2", tickets[2]));
        var fourth = await runner.RunAsync(workflow, Triggered("r3", tickets[3]));

        Assert.Equal([PipeQueueState.Started, PipeQueueState.Queued, PipeQueueState.Queued, PipeQueueState.Rejected], tickets.Select(t => t.State));
        Assert.Null(fourth);
        Assert.Equal(2, running.Find("r2")!.QueuePosition);
        Assert.Equal(PipeExecutionStatus.Queued, running.Find("r1")!.Status);

        var executions = await Task.WhenAll(first, second, third);
        Assert.All(executions, e => Assert.Equal(PipeExecutionStatus.Success, e!.Status));
        // first come, first served
        Assert.True(executions[1]!.StartedAt <= executions[2]!.StartedAt);
    }

    [Fact]
    public async Task Abandoned_or_canceled_runs_leave_the_queue_and_never_start()
    {
        using var services = PipeTestHost.Create();
        var runner = services.GetRequiredService<IPipeWorkflowRunner>();
        var running = services.GetRequiredService<IPipeRunningExecutions>();
        var executions = services.GetRequiredService<IPipeExecutionStore>();
        var workflow = new PipeWorkflowBuilder()
            .Node("slow", TestNodeIds.Slow, new { delayMs = 300 })
            .Configure(w => w.Settings.MaxQueuedRuns = 5)
            .Build();

        var first = runner.RunAsync(workflow, new PipeExecutionRequest { Mode = PipeExecutionMode.Trigger });
        var abandonedTicket = new PipeQueueTicket();
        var abandoned = runner.RunAsync(workflow, new PipeExecutionRequest { Mode = PipeExecutionMode.Trigger, QueueTicket = abandonedTicket });
        var canceled = runner.RunAsync(workflow, new PipeExecutionRequest { Mode = PipeExecutionMode.Trigger, ExecutionId = "waiting" });

        Assert.True(abandonedTicket.TryAbandon());
        Assert.True(running.Cancel("waiting"));

        Assert.Null(await abandoned);
        Assert.Null(await canceled);
        Assert.Null(running.Find("waiting"));
        await first;
        Assert.Single(await executions.ListAsync(workflow.Id));
        // too late: it has started
        Assert.False(abandonedTicket.TryStart());
    }

    [Fact]
    public async Task Manual_runs_are_never_queued()
    {
        using var services = PipeTestHost.Create();
        var runner = services.GetRequiredService<IPipeWorkflowRunner>();
        var workflow = new PipeWorkflowBuilder()
            .Node("slow", TestNodeIds.Slow, new { delayMs = 300 })
            .Configure(w => w.Settings.MaxQueuedRuns = 5)
            .Build();

        var first = runner.RunAsync(workflow);
        Assert.Null(await runner.RunAsync(workflow));
        await first;
    }

    [Fact]
    public async Task Replay_from_the_failed_node_reuses_the_stored_outputs_before_it()
    {
        using var services = PipeTestHost.Create();
        var runner = services.GetRequiredService<IPipeWorkflowRunner>();
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, new { items = new[] { new { n = 1 } } })
            .Node("fail", TestNodeIds.Fail, new { key = Guid.NewGuid().ToString(), failTimes = 1 })
            .Connect("emit", "fail")
            .Build();
        await services.GetRequiredService<IPipeWorkflowStore>().SaveAsync(workflow);

        var failed = await runner.RunAsync(workflow.Id);
        Assert.Equal(PipeExecutionStatus.Error, failed!.Status);

        // the emitted items change, but the replay works with those of the failed run
        workflow.Nodes.Single(n => n.Id == "emit").Parameters["items"] = new JsonArray(new JsonObject { ["n"] = 2 });
        await services.GetRequiredService<IPipeWorkflowStore>().SaveAsync(workflow);

        var replay = await runner.ReplayAsync(workflow.Id, failed.Id, failed.ErrorNodeId);

        Assert.Equal(PipeExecutionStatus.Success, replay.Status);
        Assert.Equal(PipeExecutionMode.Replay, replay.Mode);
        Assert.Equal(failed.Id, replay.ReplayOfExecutionId);
        Assert.True(replay.NodeRuns["emit"].Pinned);
        Assert.False(replay.NodeRuns["fail"].Pinned);
        Assert.Equal(1, replay.NodeRuns["fail"].Outputs!["main"][0].Json["n"]!.GetValue<int>());
    }

    [Fact]
    public async Task Replay_from_the_start_runs_the_current_workflow_with_the_stored_trigger_output()
    {
        using var services = PipeTestHost.Create();
        var runner = services.GetRequiredService<IPipeWorkflowRunner>();
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, new { items = new[] { new { n = 1 } } })
            .Node("echo", TestNodeIds.Echo)
            .Connect("emit", "echo")
            .Build();
        await services.GetRequiredService<IPipeWorkflowStore>().SaveAsync(workflow);
        var original = await runner.RunAsync(workflow.Id);

        workflow.Nodes.Single(n => n.Id == "emit").Parameters["items"] = new JsonArray(new JsonObject { ["n"] = 2 });
        await services.GetRequiredService<IPipeWorkflowStore>().SaveAsync(workflow);
        var replay = await runner.ReplayAsync(workflow.Id, original!.Id);

        Assert.True(replay.NodeRuns["emit"].Pinned);
        Assert.False(replay.NodeRuns["echo"].Pinned);
        Assert.Equal(1, replay.NodeRuns["echo"].Outputs!["main"][0].Json["n"]!.GetValue<int>());
        Assert.Equal(original.Environment, replay.Environment);
    }

    [Fact]
    public async Task Executions_stored_without_data_cant_be_replayed()
    {
        using var services = PipeTestHost.Create();
        var runner = services.GetRequiredService<IPipeWorkflowRunner>();
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit)
            .Configure(w => w.Settings.SaveExecutionData = false)
            .Build();
        await services.GetRequiredService<IPipeWorkflowStore>().SaveAsync(workflow);
        var original = await runner.RunAsync(workflow.Id);

        await Assert.ThrowsAsync<gView.Pipe.Abstraction.PipeException>(() => runner.ReplayAsync(workflow.Id, original!.Id));
    }

    [Fact]
    public async Task Replays_wait_for_a_free_slot_instead_of_being_skipped()
    {
        using var services = PipeTestHost.Create();
        var runner = services.GetRequiredService<IPipeWorkflowRunner>();
        var workflow = new PipeWorkflowBuilder()
            .Node("slow", TestNodeIds.Slow, new { delayMs = 200 })
            .Build();
        await services.GetRequiredService<IPipeWorkflowStore>().SaveAsync(workflow);
        var original = await runner.RunAsync(workflow.Id);

        var running = runner.RunAsync(workflow.Id);
        var replay = await runner.ReplayAsync(workflow.Id, original!.Id, "slow");

        Assert.Equal(PipeExecutionStatus.Success, replay.Status);
        Assert.Equal(PipeExecutionStatus.Success, (await running)!.Status);
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
