using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Razor.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace gView.Pipe.Razor.Tests.Components;

public class PipeExecutionListTests : BunitContext
{
    public PipeExecutionListTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddPipeCore(options =>
        {
            options.ScanApplicationDirectory = false;
            options.PluginPath = null;
        });
    }

    private async Task SeedAsync()
    {
        await Services.GetRequiredService<IPipeWorkflowStore>().SaveAsync(new PipeWorkflow { Id = "import", Name = "Nightly import" });
        var executions = Services.GetRequiredService<IPipeExecutionStore>();
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        await executions.SaveAsync(new PipeExecution
        {
            Id = "failed", WorkflowId = "import", WorkflowName = "Nightly import", Mode = PipeExecutionMode.Trigger,
            Status = PipeExecutionStatus.Error, StartedAt = start, FinishedAt = start.AddSeconds(2), ErrorNodeId = "http",
            NodeRuns = { ["http"] = new PipeNodeRun { NodeId = "http", NodeName = "Fetch orders" } }
        });
        await executions.SaveAsync(new PipeExecution
        {
            Id = "nodata", WorkflowId = "import", WorkflowName = "Nightly import", Mode = PipeExecutionMode.Manual,
            Status = PipeExecutionStatus.Success, StartedAt = start.AddMinutes(1), DataSaved = false
        });
        // older than the default time range
        await executions.SaveAsync(new PipeExecution
        {
            Id = "old", WorkflowId = "import", Status = PipeExecutionStatus.Success, StartedAt = start.AddDays(-30)
        });
    }

    [Fact]
    public async Task Lists_recent_executions_of_all_workflows_with_failed_node_and_replay_where_possible()
    {
        await SeedAsync();
        PipeExecutionSummary? opened = null;

        Render<MudPopoverProvider>();
        var list = Render<PipeExecutionList>(ps => ps.Add(p => p.OnOpen, (PipeExecutionSummary s) => opened = s));

        var rows = list.FindAll("tr.gview-pipe-execution-row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Nightly import", rows[1].TextContent);
        Assert.Contains("Fetch orders", rows[1].TextContent);
        Assert.Contains("Trigger", rows[1].TextContent);
        // stored without data: no replay button
        Assert.Empty(rows[0].QuerySelectorAll("button[aria-label=Replay]"));
        Assert.Single(rows[1].QuerySelectorAll("button[aria-label=Replay]"));

        rows[1].QuerySelector("td[data-label=Workflow]")!.Click();
        Assert.Equal("failed", opened!.Id);
    }
}
