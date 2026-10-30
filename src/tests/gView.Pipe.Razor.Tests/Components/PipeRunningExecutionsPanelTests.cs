using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Razor.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace gView.Pipe.Razor.Tests.Components;

public class PipeRunningExecutionsPanelTests : BunitContext
{
    private readonly FakeRunning _running = new();

    public PipeRunningExecutionsPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IPipeRunningExecutions>(_running);
    }

    static private PipeRunningExecutionInfo Info(string id, string workflow, PipeExecutionMode mode, PipeExecutionStatus status = PipeExecutionStatus.Running,
        string? parent = null, string[]? nodes = null, string? line = null, int startedSecondsAgo = 10)
        => new(id, workflow.ToLowerInvariant(), workflow, mode, status, DateTimeOffset.UtcNow.AddSeconds(-startedSecondsAgo),
            status == PipeExecutionStatus.Running ? null : DateTimeOffset.UtcNow, parent, nodes ?? [],
            line is null ? null : new PipeLiveLine("n", "Run Scripts", new PipeLogEntry(DateTimeOffset.UtcNow, Microsoft.Extensions.Logging.LogLevel.Information, line)));

    [Fact]
    public void Nothing_running_renders_nothing()
    {
        var panel = Render<PipeRunningExecutionsPanel>();

        Assert.Empty(panel.FindAll(".gview-pipe-running"));
    }

    [Fact]
    public void Runs_show_trigger_running_node_last_line_and_sub_workflows_under_their_caller()
    {
        _running.Infos =
        [
            Info("child", "Price Calculator", PipeExecutionMode.SubWorkflow, parent: "parent", startedSecondsAgo: 2),
            Info("other", "Nightly", PipeExecutionMode.Trigger, PipeExecutionStatus.Success, startedSecondsAgo: 5),
            Info("parent", "Sub-workflow Demo", PipeExecutionMode.Manual, nodes: ["Calculate Prices"], line: "Step 4 of 8", startedSecondsAgo: 8)
        ];
        PipeRunningExecutionInfo? opened = null;

        Render<MudPopoverProvider>();
        var panel = Render<PipeRunningExecutionsPanel>(ps => ps.Add(p => p.OnOpen, (PipeRunningExecutionInfo info) => opened = info));

        var rows = panel.FindAll(".gview-pipe-running-row");
        Assert.Equal(["Nightly", "Sub-workflow Demo", "Price Calculator"], rows.Select(r => r.QuerySelector(".gview-pipe-running-name")!.TextContent));
        Assert.Contains("finished", rows[0].ClassList);
        Assert.Contains("Calculate Prices", rows[1].TextContent);
        Assert.Contains("Run Scripts: Step 4 of 8", rows[1].TextContent);
        Assert.Contains("padding-left:30px", rows[2].GetAttribute("style"));
        // only running runs can be stopped
        Assert.Empty(rows[0].QuerySelectorAll("button[aria-label=Stop]"));
        Assert.Single(rows[1].QuerySelectorAll("button[aria-label=Stop]"));

        rows[1].Click();
        Assert.Equal("parent", opened!.ExecutionId);
    }

    private sealed class FakeRunning : IPipeRunningExecutions
    {
        public IReadOnlyList<PipeRunningExecutionInfo> Infos { get; set; } = [];

        public IReadOnlyList<PipeRunningExecutionInfo> List() => Infos.OrderByDescending(i => i.StartedAt).ToList();
        public PipeRunningExecutionInfo? Find(string executionId) => Infos.FirstOrDefault(i => i.ExecutionId == executionId);
        public IReadOnlyDictionary<string, PipeNodeRun> GetNodeRuns(string executionId) => new Dictionary<string, PipeNodeRun>();
        public PipeExecution? GetFinished(string executionId) => null;
        public IReadOnlyList<PipeLiveLine> GetLines(string executionId) => [];
        public bool Cancel(string executionId) => true;
        public event Action<string>? Changed { add { } remove { } }
    }
}
