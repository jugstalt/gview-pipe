using gView.Pipe.Abstraction.Users;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace gView.Pipe.AspNetCore.Authentication;

/// <summary>Works out the roles of a signed-in user from the configuration (ADR 0021).</summary>
static public class PipeRoles
{
    static public PipeUserRoles Resolve(ClaimsPrincipal user, PipeAuthenticationOptions options, ILogger? logger = null)
    {
        if (options.Mode == PipeAuthenticationMode.None)
        {
            return PipeUserRoles.FullAccess;
        }
        if (user.Identity?.IsAuthenticated != true)
        {
            return new PipeUserRoles(PipeRole.Viewer, new Dictionary<string, PipeRole>());
        }

        var fallback = Parse(options.DefaultRole, logger) ?? PipeRole.Admin;
        return options.Mode == PipeAuthenticationMode.Forms
            ? FromFormsUser(user.Identity.Name, options, fallback, logger)
            : FromClaims(user, options.Oidc, fallback, logger);
    }

    static private PipeUserRoles FromFormsUser(string? name, PipeAuthenticationOptions options, PipeRole fallback, ILogger? logger)
    {
        var entry = options.Users.FirstOrDefault(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));
        var global = Parse(entry?.Role, logger) ?? fallback;
        var groups = new Dictionary<string, PipeRole>(StringComparer.OrdinalIgnoreCase);
        foreach (var (group, role) in entry?.GroupRoles ?? [])
        {
            if (Parse(role, logger) is { } parsed)
            {
                groups[group] = parsed;
            }
        }
        return new PipeUserRoles(global, groups);
    }

    /// <summary>The highest mapped role wins; a group gets at least the instance role.</summary>
    static private PipeUserRoles FromClaims(ClaimsPrincipal user, PipeOidcOptions oidc, PipeRole fallback, ILogger? logger)
    {
        PipeRole? global = null;
        var groups = new Dictionary<string, PipeRole>(StringComparer.OrdinalIgnoreCase);
        foreach (var claim in user.FindAll(oidc.RoleClaimType))
        {
            if (!oidc.RoleMappings.TryGetValue(claim.Value, out var mapped))
            {
                continue;
            }
            var at = mapped.IndexOf('@');
            if (Parse(at < 0 ? mapped : mapped[..at], logger) is not { } role)
            {
                continue;
            }
            if (at < 0)
            {
                global = global is { } current && current > role ? current : role;
            }
            else
            {
                var group = mapped[(at + 1)..].Trim();
                groups[group] = groups.TryGetValue(group, out var current) && current > role ? current : role;
            }
        }

        var instance = global ?? fallback;
        return new PipeUserRoles(instance, groups.ToDictionary(g => g.Key, g => g.Value > instance ? g.Value : instance, StringComparer.OrdinalIgnoreCase));
    }

    static public PipeRole? Parse(string? role, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }
        if (Enum.TryParse<PipeRole>(role.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }
        logger?.LogWarning("Unknown role '{Role}' in Pipe:Auth; use viewer, operator, editor or admin", role);
        return null;
    }
}

/// <summary>
/// The roles of the user of the current Blazor circuit (or HTTP request), worked out on every call from the
/// configuration, so they are never stale in a long-lived circuit.
/// </summary>
internal sealed class PipeClaimsAuthorization : IPipeAuthorization
{
    private readonly PipeAuthenticationOptions _options;
    private readonly AuthenticationStateProvider? _state;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<PipeClaimsAuthorization> _logger;

    public PipeClaimsAuthorization(PipeAuthenticationOptions options, IHttpContextAccessor http, ILogger<PipeClaimsAuthorization> logger,
        IServiceProvider services)
    {
        _options = options;
        _http = http;
        _logger = logger;
        // only inside a Blazor circuit; plain HTTP requests use the HttpContext
        _state = services.GetService(typeof(AuthenticationStateProvider)) as AuthenticationStateProvider;
    }

    public async Task<PipeUserRoles> GetRolesAsync()
    {
        ClaimsPrincipal? user = null;
        if (_state is not null)
        {
            try
            {
                user = (await _state.GetAuthenticationStateAsync()).User;
            }
            catch (InvalidOperationException)
            {
                // outside of a circuit the provider has no state
            }
        }
        user ??= _http.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        // the name for the audit log (ADR 0022); the address is known while there is a request
        var name = user.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(user.Identity.Name)
            ? user.Identity.Name
            : _http.HttpContext?.Connection.RemoteIpAddress is { } address ? $"anonymous ({address})" : "anonymous";
        return PipeRoles.Resolve(user, _options, _logger) with { UserName = name };
    }
}
