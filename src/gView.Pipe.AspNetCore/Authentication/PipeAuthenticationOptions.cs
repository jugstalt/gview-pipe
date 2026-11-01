namespace gView.Pipe.AspNetCore.Authentication;

public enum PipeAuthenticationMode
{
    /// <summary>No login; everyone who can reach the app may use it.</summary>
    None,
    /// <summary>A login form with users from the configuration.</summary>
    Forms,
    /// <summary>OpenID Connect against an external identity provider.</summary>
    Oidc
}

/// <summary>Settings of the login (section <c>Pipe:Auth</c> in the Shell, see ADR 0014).</summary>
public class PipeAuthenticationOptions
{
    public PipeAuthenticationMode Mode { get; set; } = PipeAuthenticationMode.None;

    /// <summary>Lifetime of the session cookie; every request extends it (sliding).</summary>
    public int SessionMinutes { get; set; } = 480;

    /// <summary>Honors <c>X-Forwarded-For</c>/<c>-Proto</c> of a reverse proxy, so OIDC builds https redirect URIs.</summary>
    public bool UseForwardedHeaders { get; set; }

    /// <summary>Users of the <see cref="PipeAuthenticationMode.Forms"/> mode.</summary>
    public List<PipeFormsUser> Users { get; set; } = [];

    /// <summary>Login attempts per minute and client address in the forms mode.</summary>
    public int LoginAttemptsPerMinute { get; set; } = 10;

    public PipeOidcOptions Oidc { get; set; } = new();
}

public class PipeFormsUser
{
    public string Name { get; set; } = "";

    /// <summary>Plain text; logs a warning on start. <see cref="PasswordHash"/> wins when both are set.</summary>
    public string? Password { get; set; }

    /// <summary>Created with <c>--hash-password</c>, see <see cref="PipePasswordHasher"/>.</summary>
    public string? PasswordHash { get; set; }
}

public class PipeOidcOptions
{
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string? ClientSecret { get; set; }
    /// <summary>Empty: <c>openid profile role</c>. Not a default list, because configuration binding appends to it.</summary>
    public List<string> Scopes { get; set; } = [];

    /// <summary>Claim that carries the roles; IdentityServer.NET uses <c>role</c>.</summary>
    public string RoleClaimType { get; set; } = "role";

    /// <summary>Claim shown as the user name.</summary>
    public string NameClaimType { get; set; } = "name";

    /// <summary>A user needs one of these roles. Empty: every authenticated user may enter.</summary>
    public List<string> RequiredRoles { get; set; } = [];

    public bool RequireHttpsMetadata { get; set; } = true;
}
