using System.Security.Claims;

namespace gView.Pipe.AspNetCore.Authentication;

static public class PipeAuthentication
{
    /// <summary>Policy of the UI endpoints; in the mode <see cref="PipeAuthenticationMode.None"/> it allows everyone.</summary>
    public const string PolicyName = "gview-pipe-user";

    public const string LoginRateLimitPolicy = "gview-pipe-login";

    /// <summary>The page with the form (host); the form posts to <see cref="SignInPath"/>, as a page takes posts too.</summary>
    public const string LoginPath = "/account/login";
    public const string SignInPath = "/account/signin";
    public const string ChallengePath = "/account/challenge";
    public const string LogoutPath = "/account/logout";
    public const string DeniedPath = "/account/denied";

    /// <summary>True when the user may enter: in OIDC mode with one of the required roles, if any are configured.</summary>
    static public bool IsAllowed(ClaimsPrincipal user, PipeAuthenticationOptions options)
    {
        if (options.Mode == PipeAuthenticationMode.None)
        {
            return true;
        }
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }
        if (options.Mode != PipeAuthenticationMode.Oidc || options.Oidc.RequiredRoles.Count == 0)
        {
            return true;
        }

        return user.FindAll(options.Oidc.RoleClaimType)
            .Any(claim => options.Oidc.RequiredRoles.Contains(claim.Value, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>A return URL of this app only (no other hosts, no <c>//host</c>); otherwise <c>/</c>.</summary>
    static public string SafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || returnUrl[0] != '/')
        {
            return "/";
        }
        if (returnUrl.Length > 1 && (returnUrl[1] == '/' || returnUrl[1] == '\\'))
        {
            return "/";
        }
        return returnUrl;
    }
}
