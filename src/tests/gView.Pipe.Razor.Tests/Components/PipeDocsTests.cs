using System.Globalization;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Razor.Components;
using gView.Pipe.Razor.Editor;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace gView.Pipe.Razor.Tests.Components;

public class PipeDocsTests : BunitContext
{
    public PipeDocsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddPipeCore(options =>
        {
            options.ScanApplicationDirectory = false;
            options.PluginPath = null;
        });
        Services.AddSingleton(new PipeDocs("docs"));
    }

    [Fact]
    public void Links_to_the_public_site_by_default()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        Assert.Equal("https://docs.gviewonline.com/other/gview-pipe/index.html", new PipeDocs().Url());
    }

    [Fact]
    public void Node_pages_follow_the_generated_reference()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        var docs = new PipeDocs("docs/");

        Assert.Equal("docs/user-guide/nodes/data.html#edit-fields", docs.NodeUrl("Data", "Edit Fields"));
    }

    [Fact]
    public void Uses_the_german_pages_for_a_german_ui()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("de-AT");
            Assert.Equal("docs/de/user-guide/editor.html", new PipeDocs("docs").Url("user-guide/editor.html"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }

    [Fact]
    public void The_workflow_panel_is_divided_into_sections_and_only_general_is_open()
    {
        Render<MudBlazor.MudPopoverProvider>();
        var panel = Render<PipeWorkflowPanel>(p => p.Add(x => x.Workflow, new PipeWorkflow { Id = "w1", Name = "Demo" }));

        var titles = panel.FindAll(".mud-expand-panel .mud-typography-subtitle2").Select(e => e.TextContent.Trim()).ToList();
        Assert.Equal(["General", "Execution", "Environment", "Variables", "Execution data", "User portal"], titles);
        Assert.Single(panel.FindAll(".mud-expand-panel.mud-panel-expanded"));
    }
}
