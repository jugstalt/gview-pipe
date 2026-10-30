using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Core.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace gView.Pipe.Core.Tests.Engine;

public sealed class PipeExecutionMonitorTests : IDisposable
{
    private readonly ManualTime _time = new();
    private readonly ServiceProvider _services;

    public PipeExecutionMonitorTests()
    {
        _services = PipeTestHost.Create(s => s.AddSingleton<TimeProvider>(_time));
    }

    public void Dispose() => _services.Dispose();

    private IPipeRunningExecutions Monitor => _services.GetRequiredService<IPipeRunningExecutions>();
    private IPipeWorkflowRunner Runner => _services.GetRequiredService<IPipeWorkflowRunner>();

    static private async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(20);
        }
        Assert.True(condition(), "condition not met in time");
    }

    [Fact]
    public async Task Running_execution_shows_its_node_and_live_output_and_stays_after_finishing()
    {
        var workflow = new PipeWorkflowBuilder("Chatty", "chatty")
            .Node("chatty", TestNodeIds.Chatty)
            .Node("slow", TestNodeIds.Slow, new { delayMs = 400 })
            .Connect("chatty", "slow")
            .Build();
        var changes = 0;
        Monitor.Changed += _ => Interlocked.Increment(ref changes);

        var run = Runner.RunAsync(workflow, new PipeExecutionRequest { Mode = PipeExecutionMode.Trigger });

        await WaitUntil(() => Monitor.List().FirstOrDefault()?.RunningNodes.Contains("slow") == true);
        var running = Monitor.List().Single();
        Assert.True(running.IsRunning);
        Assert.Equal(PipeExecutionMode.Trigger, running.Mode);
        Assert.Equal("step 3", running.LastLine!.Entry.Message);
        Assert.Equal(3, Monitor.GetLines(running.ExecutionId).Count);
        Assert.Null(Monitor.GetFinished(running.ExecutionId));

        var execution = await run;

        var finished = Monitor.Find(execution!.Id)!;
        Assert.Equal(PipeExecutionStatus.Success, finished.Status);
        Assert.Same(execution, Monitor.GetFinished(execution.Id));
        Assert.True(changes > 5);

        // finished runs disappear after a minute
        _time.Now += TimeSpan.FromSeconds(61);
        Assert.Empty(Monitor.List());
    }

    [Fact]
    public async Task Cancel_stops_a_running_execution()
    {
        var workflow = new PipeWorkflowBuilder("Slow", "slow").Node("slow", TestNodeIds.Slow, new { delayMs = 10_000 }).Build();

        var run = Runner.RunAsync(workflow);
        await WaitUntil(() => Monitor.List().FirstOrDefault()?.RunningNodes.Count == 1);

        Assert.True(Monitor.Cancel(Monitor.List().Single().ExecutionId));
        var execution = await run;

        Assert.Equal(PipeExecutionStatus.Canceled, execution!.Status);
        Assert.False(Monitor.Cancel(execution.Id));
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
