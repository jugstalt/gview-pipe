using System.Security.Claims;
using gView.Pipe.AspNetCore.Authentication;

namespace gView.Pipe.AspNetCore.Tests.Authentication;

public class PipeAuthenticationTests
{
    static private ClaimsPrincipal User(params string[] roles) => new(new ClaimsIdentity(
        [new Claim("name", "jane"), .. roles.Select(r => new Claim("role", r))],
        "test", "name", "role"));

    static private PipeAuthenticationOptions Oidc(params string[] requiredRoles) => new()
    {
        Mode = PipeAuthenticationMode.Oidc,
        Oidc = new PipeOidcOptions { RequiredRoles = [.. requiredRoles] }
    };

    [Fact]
    public void IsAllowed_None_AllowsAnonymous()
    {
        Assert.True(PipeAuthentication.IsAllowed(new ClaimsPrincipal(new ClaimsIdentity()), new PipeAuthenticationOptions()));
    }

    [Fact]
    public void IsAllowed_Forms_NeedsAnAuthenticatedUser()
    {
        var options = new PipeAuthenticationOptions { Mode = PipeAuthenticationMode.Forms };

        Assert.False(PipeAuthentication.IsAllowed(new ClaimsPrincipal(new ClaimsIdentity()), options));
        Assert.True(PipeAuthentication.IsAllowed(User(), options));
    }

    [Fact]
    public void IsAllowed_Oidc_WithoutRequiredRoles_AllowsEveryAuthenticatedUser()
    {
        Assert.True(PipeAuthentication.IsAllowed(User(), Oidc()));
    }

    [Fact]
    public void IsAllowed_Oidc_NeedsOneOfTheRoles()
    {
        var options = Oidc("gview-pipe", "admins");

        Assert.False(PipeAuthentication.IsAllowed(User("users"), options));
        Assert.True(PipeAuthentication.IsAllowed(User("users", "GVIEW-PIPE"), options));
        Assert.True(PipeAuthentication.IsAllowed(User("admins"), options));
    }

    [Fact]
    public void IsAllowed_Oidc_UsesTheConfiguredRoleClaim()
    {
        var options = Oidc("gview-pipe");
        options.Oidc.RoleClaimType = "groups";

        Assert.False(PipeAuthentication.IsAllowed(User("gview-pipe"), options));
        Assert.True(PipeAuthentication.IsAllowed(new ClaimsPrincipal(new ClaimsIdentity([new Claim("groups", "gview-pipe")], "test")), options));
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/workflows/1?x=2", "/workflows/1?x=2")]
    [InlineData("https://evil.example", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("workflows", "/")]
    public void SafeReturnUrl_KeepsOnlyLocalPaths(string? returnUrl, string expected)
    {
        Assert.Equal(expected, PipeAuthentication.SafeReturnUrl(returnUrl));
    }
}
