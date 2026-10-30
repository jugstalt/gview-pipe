using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Razor.Editor;
using Microsoft.Extensions.Logging;
using MudBlazor;
using MudBlazor.Services;
using System.Text.Json.Nodes;

namespace gView.Pipe.Razor.Tests.Editor;

public class PipeNodeRunViewTests : BunitContext
{
    public PipeNodeRunViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    static private PipeNodeRun Run(bool withData = true) => new()
    {
        NodeId = "n",
        NodeName = "Node",
        Status = PipeNodeRunStatus.Success,
        InputCounts = new() { ["main"] = 1 },
        OutputCounts = new() { ["main"] = 1 },
        Inputs = withData ? new() { ["main"] = [new PipeItem(new JsonObject { ["in"] = "before" })] } : null,
        Outputs = withData ? new() { ["main"] = [new PipeItem(new JsonObject { ["out"] = "after" })] } : null,
        Logs = [new PipeLogEntry(DateTimeOffset.UtcNow, LogLevel.Warning, "careful")]
    };

    [Fact]
    public void Shows_input_output_and_log_tabs_with_counts()
    {
        Render<MudPopoverProvider>();
        var view = Render<PipeNodeRunView>(ps => ps.Add(p => p.Run, Run()));

        var tabs = view.FindComponents<MudTabPanel>().Select(t => t.Instance.Text).ToList();
        Assert.Contains("Input (1)", tabs);
        Assert.Contains("Output (1)", tabs);
        Assert.Contains("Logs (1)", tabs);

        // output is the tab shown first
        Assert.Contains("after", view.Markup);
    }

    [Fact]
    public void Executions_without_item_data_show_counts_only()
    {
        Render<MudPopoverProvider>();
        var view = Render<PipeNodeRunView>(ps => ps.Add(p => p.Run, Run(withData: false)));

        Assert.Contains("not saved", view.Markup);
        Assert.DoesNotContain("after", view.Markup);
    }
}
