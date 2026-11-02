using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace gView.Pipe.Plugins.Tests.Nodes;

/// <summary>The Recovery Trigger and the open failure of a workflow (ADR 0025).</summary>
public sealed class RecoveryTests : IDisposable
{
    private readonly ServiceProvider _services = PluginTestHost.Create();

    public void Dispose() => _services.Dispose();

    private IPipeWorkflowRunner Runner => _services.GetRequiredService<IPipeWorkflowRunner>();

    private IPipeWorkflowHealthStore Health => _services.GetRequiredService<IPipeWorkflowHealthStore>();

    private Task SaveAsync(PipeWorkflow workflow) => _services.GetRequiredService<IPipeWorkflowStore>().SaveAsync(workflow);

    private Task<IReadOnlyList<PipeExecution>> ExecutionsAsync(string workflowId)
        => _services.GetRequiredService<IPipeExecutionStore>().ListAsync(workflowId);

    static private PipeExecutionRequest Triggered => new() { Mode = PipeExecutionMode.Trigger };

    /// <summary>Error workflow with both entry points.</summary>
    static private PipeWorkflow Alerts(int throttleMinutes = 15) => new PipeWorkflowBuilder("Alerts", "alerts")
        .Node("error", PipeBuiltInNodes.ErrorTrigger, new { throttleMinutes })
        .Node("failed", PipeBuiltInNodes.NoOp)
        .Node("recovery", PipeBuiltInNodes.RecoveryTrigger)
        .Node("recovered", PipeBuiltInNodes.NoOp)
        .Connect("error", "failed")
        .Connect("recovery", "recovered")
        .Build();

    static private PipeWorkflow Failing() => new PipeWorkflowBuilder("Nightly", "nightly")
        .Node("start", PipeBuiltInNodes.ManualTrigger)
        .Node("code", PipeBuiltInNodes.Code, new { code = "throw new Error('database down');" })
        .Connect("start", "code")
        .Configure(w => w.Settings.ErrorWorkflowId = "alerts")
        .Build();

    static private PipeWorkflow Fixed() => new PipeWorkflowBuilder("Nightly", "nightly")
        .Node("start", PipeBuiltInNodes.ManualTrigger)
        .Node("noop", PipeBuiltInNodes.NoOp)
        .Connect("start", "noop")
        .Configure(w => w.Settings.ErrorWorkflowId = "alerts")
        .Build();

    [Fact]
    public async Task A_success_after_a_reported_failure_starts_the_recovery_trigger_once()
    {
        await SaveAsync(Alerts());
        var first = await Runner.RunAsync(Failing(), Triggered);
        await Runner.RunAsync(Failing(), Triggered);

        var health = await Health.GetAsync("nightly");
        Assert.Equal(2, health!.Failures);
        Assert.Equal(first!.StartedAt, health.FailingSince);
        Assert.True(health.Notified);

        var ok = await Runner.RunAsync(Fixed(), Triggered);
        await Runner.RunAsync(Fixed(), Triggered);

        var recoveries = (await ExecutionsAsync("alerts")).Where(e => e.NodeRuns.ContainsKey("recovered")).ToList();
        var recovery = Assert.Single(recoveries);
        Assert.Equal(PipeExecutionMode.Error, recovery.Mode);
        var item = Assert.Single(recovery.NodeRuns["recovered"].Outputs!["main"]).Json;
        Assert.Equal("Nightly", item["workflow"]!["name"]!.GetValue<string>());
        Assert.Equal(ok!.Id, item["execution"]!["id"]!.GetValue<string>());
        Assert.Equal(2, item["failures"]!.GetValue<int>());
        Assert.NotNull(item["failedSince"]);
        Assert.Null(await Health.GetAsync("nightly"));
    }

    [Fact]
    public async Task Manual_runs_neither_start_nor_end_a_failure()
    {
        await SaveAsync(Alerts());
        await Runner.RunAsync(Failing(), Triggered);

        await Runner.RunAsync(Fixed(), new PipeExecutionRequest { Mode = PipeExecutionMode.Manual });

        Assert.NotNull(await Health.GetAsync("nightly"));
        Assert.DoesNotContain(await ExecutionsAsync("alerts"), e => e.NodeRuns.ContainsKey("recovered"));
    }

    [Fact]
    public async Task No_recovery_is_reported_when_the_failure_was_never_reported()
    {
        // the first failure is reported to a throttled error workflow, so only the next ones are suppressed:
        // here the error workflow has no entry for the failure at all, so nothing was reported
        await SaveAsync(new PipeWorkflowBuilder("Alerts", "alerts")
            .Node("recovery", PipeBuiltInNodes.RecoveryTrigger)
            .Node("recovered", PipeBuiltInNodes.NoOp)
            .Connect("recovery", "recovered")
            .Build());

        await Runner.RunAsync(Failing(), Triggered);
        await Runner.RunAsync(Fixed(), Triggered);

        Assert.Empty(await ExecutionsAsync("alerts"));
        Assert.Null(await Health.GetAsync("nightly"));
    }

    [Fact]
    public async Task An_error_workflow_without_recovery_trigger_just_ends_the_failure()
    {
        await SaveAsync(new PipeWorkflowBuilder("Alerts", "alerts")
            .Node("error", PipeBuiltInNodes.ErrorTrigger)
            .Node("failed", PipeBuiltInNodes.NoOp)
            .Connect("error", "failed")
            .Build());

        await Runner.RunAsync(Failing(), Triggered);
        await Runner.RunAsync(Fixed(), Triggered);

        Assert.Single(await ExecutionsAsync("alerts"));
        Assert.Null(await Health.GetAsync("nightly"));
    }
}
