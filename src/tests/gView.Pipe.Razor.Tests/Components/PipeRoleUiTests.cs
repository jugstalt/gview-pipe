using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Users;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Razor.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace gView.Pipe.Razor.Tests.Components;

/// <summary>What the components show and allow for a role (ADR 0021).</summary>
public class PipeRoleUiTests : BunitContext
{
    private sealed class FixedRoles(PipeUserRoles roles) : IPipeAuthorization
    {
        public Task<PipeUserRoles> GetRolesAsync() => Task.FromResult(roles);
    }

    private void Setup(PipeRole global, params (string Group, PipeRole Role)[] groups)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddPipeCore(options =>
        {
            options.ScanApplicationDirectory = false;
            options.PluginPath = null;
        });
        // registered last, so it wins over the full access of AddPipeCore()
        Services.AddSingleton<IPipeAuthorization>(new FixedRoles(new PipeUserRoles(global, groups.ToDictionary(g => g.Group, g => g.Role))));
    }

    private async Task SeedAsync()
    {
        await Services.GetRequiredService<IPipeWorkflowGroupStore>().SaveAsync(new PipeWorkflowGroup { Id = "billing", Name = "Billing" });
        var workflows = Services.GetRequiredService<IPipeWorkflowStore>();
        await workflows.SaveAsync(new PipeWorkflow { Id = "invoice", Name = "Send invoices", GroupId = "billing" });
        await workflows.SaveAsync(new PipeWorkflow { Id = "loose", Name = "Loose workflow" });
    }

    private IRenderedComponent<PipeWorkflowList> RenderList(bool showGroups = false, string? groupId = null)
    {
        Render<MudPopoverProvider>();
        return Render<PipeWorkflowList>(ps => ps
            .Add(p => p.ShowGroups, showGroups)
            .Add(p => p.GroupId, groupId)
            .Add(p => p.OnOpen, (string _) => { }));
    }

    [Fact]
    public void Admin_pages_tell_a_viewer_that_they_are_not_allowed()
    {
        Setup(PipeRole.Editor);

        var page = Render<PipeRequireRole>(ps => ps
            .Add(p => p.Permission, PipePermission.Administer)
            .AddChildContent("<p class=\"secret\">credentials</p>"));

        page.WaitForAssertion(() => Assert.Contains("needs the role admin", page.Markup));
        Assert.Empty(page.FindAll(".secret"));
    }

    [Fact]
    public async Task A_viewer_sees_workflows_but_no_buttons_to_change_or_run_them()
    {
        Setup(PipeRole.Viewer);
        await SeedAsync();

        var list = RenderList(showGroups: true);

        list.WaitForAssertion(() => Assert.Contains("Loose workflow", list.Markup));
        Assert.DoesNotContain("New workflow", list.Markup);
        Assert.DoesNotContain("New group", list.Markup);
        Assert.Empty(list.FindAll("button[aria-label=Delete]"));
        Assert.All(list.FindAll("button[aria-label=Run]"), b => Assert.True(b.HasAttribute("disabled")));
        // no menu on the group tile either
        Assert.Empty(list.FindAll(".gview-pipe-group-tile [aria-label='Group actions']"));
    }

    [Fact]
    public async Task A_group_role_allows_more_in_that_group_only()
    {
        Setup(PipeRole.Viewer, ("billing", PipeRole.Editor));
        await SeedAsync();

        var billing = RenderList(groupId: "billing");
        billing.WaitForAssertion(() => Assert.Contains("Send invoices", billing.Markup));
        Assert.Contains("New workflow", billing.Markup);
        Assert.Single(billing.FindAll("button[aria-label=Delete]"));
        Assert.All(billing.FindAll("button[aria-label=Run]"), b => Assert.False(b.HasAttribute("disabled")));
    }
}
