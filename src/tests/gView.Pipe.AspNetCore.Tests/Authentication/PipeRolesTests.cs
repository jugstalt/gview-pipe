using gView.Pipe.Abstraction.Users;
using gView.Pipe.AspNetCore.Authentication;
using System.Security.Claims;

namespace gView.Pipe.AspNetCore.Tests.Authentication;

public class PipeRolesTests
{
    static private ClaimsPrincipal User(string name, params string[] roles) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, name), .. roles.Select(r => new Claim("role", r))],
        "test", ClaimTypes.Name, "role"));

    static private PipeAuthenticationOptions Forms(string defaultRole = "admin") => new()
    {
        Mode = PipeAuthenticationMode.Forms,
        DefaultRole = defaultRole,
        Users =
        [
            new PipeFormsUser { Name = "ada", Role = "viewer", GroupRoles = new(StringComparer.OrdinalIgnoreCase) { ["billing"] = "editor" } },
            new PipeFormsUser { Name = "grace" }
        ]
    };

    static private PipeAuthenticationOptions Oidc(string defaultRole = "viewer") => new()
    {
        Mode = PipeAuthenticationMode.Oidc,
        DefaultRole = defaultRole,
        Oidc = new PipeOidcOptions
        {
            RoleMappings = new(StringComparer.OrdinalIgnoreCase)
            {
                ["pipe-ops"] = "operator",
                ["pipe-admins"] = "admin",
                ["pipe-billing"] = "editor@billing",
                ["pipe-hr"] = "viewer@hr"
            }
        }
    };

    [Fact]
    public void Without_login_everyone_is_admin()
    {
        var roles = PipeRoles.Resolve(new ClaimsPrincipal(new ClaimsIdentity()), new PipeAuthenticationOptions());

        Assert.True(roles.Can(PipePermission.Administer));
    }

    [Fact]
    public void Forms_users_get_their_role_and_group_roles_replace_it()
    {
        var roles = PipeRoles.Resolve(User("ADA"), Forms());

        Assert.Equal(PipeRole.Viewer, roles.Role());
        Assert.Equal(PipeRole.Editor, roles.Role("billing"));
        Assert.False(roles.Can(PipePermission.Run));
        Assert.True(roles.Can(PipePermission.Edit, "billing"));
        // credentials and API keys only look at the instance role
        Assert.False(roles.Can(PipePermission.Administer, "billing"));
    }

    [Fact]
    public void Users_without_a_role_get_the_default()
    {
        Assert.Equal(PipeRole.Admin, PipeRoles.Resolve(User("grace"), Forms()).Role());
        Assert.Equal(PipeRole.Viewer, PipeRoles.Resolve(User("grace"), Forms("viewer")).Role());
        Assert.Equal(PipeRole.Viewer, PipeRoles.Resolve(User("unknown"), Forms("viewer")).Role());
    }

    [Fact]
    public void Oidc_takes_the_highest_mapped_role_and_groups_at_least_that()
    {
        var roles = PipeRoles.Resolve(User("jane", "pipe-ops", "pipe-billing", "pipe-hr", "other"), Oidc());

        Assert.Equal(PipeRole.Operator, roles.Role());
        Assert.Equal(PipeRole.Editor, roles.Role("billing"));
        // a lower group mapping doesn't take away the instance role
        Assert.Equal(PipeRole.Operator, roles.Role("hr"));
        Assert.Equal(PipeRole.Admin, PipeRoles.Resolve(User("root", "pipe-ops", "pipe-admins"), Oidc()).Role());
    }

    [Fact]
    public void Oidc_without_a_mapped_claim_gets_the_default()
    {
        Assert.Equal(PipeRole.Viewer, PipeRoles.Resolve(User("jane", "other"), Oidc()).Role());
        Assert.Equal(PipeRole.Operator, PipeRoles.Resolve(User("jane"), Oidc("operator")).Role());
    }

    [Fact]
    public void Unknown_roles_are_ignored()
    {
        var options = Forms("viewer");
        options.Users[1].Role = "superuser";

        Assert.Equal(PipeRole.Viewer, PipeRoles.Resolve(User("grace"), options).Role());
    }
}
