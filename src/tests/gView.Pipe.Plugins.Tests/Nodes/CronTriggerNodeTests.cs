using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Core.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace gView.Pipe.Plugins.Tests.Nodes;

public class CronTriggerNodeTests
{
    [Fact]
    public async Task Activation_registers_the_schedule_and_firing_runs_the_workflow()
    {
        var host = new FakeTriggerHost();
        using var services = PluginTestHost.Create(s => s.Replace(ServiceDescriptor.Singleton<IPipeTriggerHost>(host)));

        var workflow = new PipeWorkflowBuilder()
            .Node("cron", PipeBuiltInNodes.CronTrigger, new { expression = "*/5 * * * *", timeZone = "UTC" })
            .Node("noop", PipeBuiltInNodes.NoOp)
            .Connect("cron", "noop")
            .Configure(w => w.Active = true)
            .Build();

        var manager = services.GetRequiredService<IPipeTriggerManager>();
        await manager.ActivateAsync(workflow);

        var (expression, callback) = Assert.Single(host.Registrations);
        Assert.Equal("*/5 * * * *", expression);

        await callback(CancellationToken.None);

        var execution = Assert.Single(await services.GetRequiredService<IPipeExecutionStore>().ListAsync(workflow.Id));
        Assert.Equal(PipeExecutionStatus.Success, execution.Status);
        Assert.Equal("*/5 * * * *", execution.NodeRuns["noop"].Outputs!["main"][0].Json["expression"]!.GetValue<string>());

        await manager.DeactivateAsync(workflow.Id);
        Assert.True(host.Disposed);
    }

    private sealed class FakeTriggerHost : IPipeTriggerHost
    {
        public List<(string Expression, Func<CancellationToken, Task> Callback)> Registrations { get; } = new();
        public bool Disposed { get; private set; }

        public IDisposable RegisterSchedule(string cronExpression, TimeZoneInfo timeZone, Func<CancellationToken, Task> callback)
        {
            Registrations.Add((cronExpression, callback));
            return new Handle(this);
        }

        private sealed class Handle(FakeTriggerHost host) : IDisposable
        {
            public void Dispose() => host.Disposed = true;
        }
    }
}
