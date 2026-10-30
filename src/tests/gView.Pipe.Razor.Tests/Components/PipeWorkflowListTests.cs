using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Razor.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace gView.Pipe.Razor.Tests.Components;

public class PipeWorkflowListTests : BunitContext
{
    public PipeWorkflowListTests()
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
        var groups = Services.GetRequiredService<IPipeWorkflowGroupStore>();
        await groups.SaveAsync(new PipeWorkflowGroup { Id = "billing", Name = "Billing", Description = "Invoices" });
        await groups.SaveAsync(new PipeWorkflowGroup { Id = "empty", Name = "Empty" });

        var workflows = Services.GetRequiredService<IPipeWorkflowStore>();
        await workflows.SaveAsync(new PipeWorkflow { Id = "invoice", Name = "Send invoices", GroupId = "billing", Active = true });
        await workflows.SaveAsync(new PipeWorkflow { Id = "remind", Name = "Payment reminder", GroupId = "billing" });
        await workflows.SaveAsync(new PipeWorkflow { Id = "loose", Name = "Loose workflow" });
        // its group doesn't exist here (copied from another installation): shown without group
        await workflows.SaveAsync(new PipeWorkflow { Id = "foreign", Name = "Foreign workflow", GroupId = "elsewhere" });
    }

    private IRenderedComponent<PipeWorkflowList> RenderList(bool showGroups = false, string? groupId = null)
    {
        Render<MudPopoverProvider>();
        return Render<PipeWorkflowList>(ps => ps
            .Add(p => p.ShowGroups, showGroups)
            .Add(p => p.GroupId, groupId)
            .Add(p => p.OnOpen, (string _) => { }));
    }

    static private IEnumerable<string> RowNames(IRenderedComponent<PipeWorkflowList> list)
        => list.FindAll(".gview-pipe-workflow-drag .mud-typography-body2").Select(e => e.TextContent.Trim());

    [Fact]
    public async Task Start_page_shows_group_tiles_and_the_workflows_without_known_group()
    {
        await SeedAsync();

        var list = RenderList(showGroups: true);

        list.WaitForAssertion(() =>
        {
            var tiles = list.FindAll(".gview-pipe-group-tile");
            Assert.Equal(["Billing", "Empty"], tiles.Select(t => t.QuerySelector(".gview-pipe-group-tile-name")!.TextContent.Trim()));
            Assert.Contains("2 workflows", tiles[0].TextContent);
            Assert.Contains("1 active", tiles[0].TextContent);
            Assert.Equal(["Foreign workflow", "Loose workflow"], RowNames(list));
        });
    }

    [Fact]
    public async Task Search_lists_matching_workflows_of_all_groups_with_their_group()
    {
        await SeedAsync();
        var list = RenderList(showGroups: true);
        list.WaitForAssertion(() => Assert.NotEmpty(list.FindAll(".gview-pipe-group-tile")));

        list.Find(".gview-pipe-workflow-search input").Input("in");

        list.WaitForAssertion(() =>
        {
            Assert.Empty(list.FindAll(".gview-pipe-group-tile"));
            Assert.Equal(["Payment reminder", "Send invoices"], RowNames(list).Order());
            Assert.Equal(2, list.FindAll(".gview-pipe-group-label").Count(l => l.TextContent == "Billing"));
        });
    }

    [Fact]
    public async Task Group_page_lists_only_the_group_and_creates_new_workflows_in_it()
    {
        await SeedAsync();
        string? opened = null;
        Render<MudPopoverProvider>();
        var list = Render<PipeWorkflowList>(ps => ps
            .Add(p => p.GroupId, "billing")
            .Add(p => p.OnOpen, (string id) => opened = id));

        list.WaitForAssertion(() => Assert.Equal(["Payment reminder", "Send invoices"], RowNames(list)));
        Assert.Contains("Invoices", list.Markup);

        await list.InvokeAsync(() => list.FindAll("button").First(b => b.TextContent.Contains("New workflow")).Click());

        var created = await Services.GetRequiredService<IPipeWorkflowStore>().GetAsync(opened!);
        Assert.Equal("billing", created!.GroupId);
    }
}
