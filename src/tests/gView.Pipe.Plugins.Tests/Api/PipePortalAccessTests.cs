using gView.Pipe.Abstraction.Portal;
using gView.Pipe.Abstraction.Workflows;

namespace gView.Pipe.Plugins.Tests.Api;

/// <summary>Which portal roles see a workflow (ADR 0030).</summary>
public sealed class PipePortalAccessTests
{
    static private PipeWorkflow Workflow(params string[] roles) => new()
    {
        Id = "w",
        Settings = { Portal = new PipeWorkflowPortalSettings { Published = true, Roles = roles.Length == 0 ? null : roles.ToList() } }
    };

    static private PipeWorkflowGroup Group(params string[] roles) => new() { Id = "g", PortalRoles = roles.Length == 0 ? null : roles.ToList() };

    [Fact]
    public void Without_roles_on_the_workflow_and_the_group_everybody_may()
    {
        Assert.True(PipePortalAccess.Allows([], Workflow(), Group()));
        Assert.True(PipePortalAccess.Allows([], Workflow(), null));
        Assert.True(PipePortalAccess.Allows(["anything"], Workflow(), Group()));
    }

    [Fact]
    public void The_roles_of_the_workflow_decide_before_those_of_the_group()
    {
        // the workflow names hr: finance of the group doesn't count
        Assert.True(PipePortalAccess.Allows(["hr"], Workflow("hr"), Group("finance")));
        Assert.False(PipePortalAccess.Allows(["finance"], Workflow("hr"), Group("finance")));
    }

    [Fact]
    public void A_workflow_without_roles_takes_those_of_its_group()
    {
        Assert.True(PipePortalAccess.Allows(["finance"], Workflow(), Group("finance", "audit")));
        Assert.True(PipePortalAccess.Allows(["audit"], Workflow(), Group("finance", "audit")));
        Assert.False(PipePortalAccess.Allows(["hr"], Workflow(), Group("finance", "audit")));
        Assert.False(PipePortalAccess.Allows([], Workflow(), Group("finance")));
    }

    [Fact]
    public void Roles_are_compared_ignoring_case()
    {
        Assert.True(PipePortalAccess.Allows(["HR"], Workflow("hr"), null));
    }

    [Fact]
    public void Roles_typed_as_a_list_are_cleaned_up()
    {
        Assert.Equal(["hr", "finance", "it ops"], PipePortalAccess.Parse(" hr, finance ;; HR\nit ops "));
        Assert.Null(PipePortalAccess.Parse("  , ;"));
        Assert.Null(PipePortalAccess.Parse(null));
    }
}
