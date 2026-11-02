using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;

namespace gView.Pipe.AspNetCore.Authentication;

/// <summary>The HTTP side of the login; the pages with the form and the "no access" text belong to the host.</summary>
static internal class PipeAuthenticationEndpoints
{
    static public async Task<IResult> LoginAsync(HttpContext context, PipeAuthenticationOptions options, IAntiforgery antiforgery)
    {
        if (options.Mode != PipeAuthenticationMode.Forms)
        {
            return Results.NotFound();
        }
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.BadRequest();
        }

        var form = await context.Request.ReadFormAsync();
        var name = form["username"].ToString().Trim();
        var password = form["password"].ToString();
        var returnUrl = PipeAuthentication.SafeReturnUrl(form["returnUrl"]);

        var user = options.Users.FirstOrDefault(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));
        if (user is null || !PipePasswordHasher.Verify(user, password))
        {
            // slows down guessing; the rate limiter caps the attempts per address
            await context.RequestServices.GetRequiredService<PipeAuditRefusals>()
                .RefusedAsync(context.Connection.RemoteIpAddress?.ToString() ?? "", "signInFailed", $"user '{name}'");
            await Task.Delay(TimeSpan.FromSeconds(1));
            return Results.Redirect($"{context.Request.PathBase}{PipeAuthentication.LoginPath}?error=1&returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user.Name)], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.Redirect($"{context.Request.PathBase}{returnUrl}");
    }

    static public IResult Challenge(HttpContext context, PipeAuthenticationOptions options, string? returnUrl)
    {
        if (options.Mode != PipeAuthenticationMode.Oidc)
        {
            return Results.NotFound();
        }
        return Results.Challenge(
            new AuthenticationProperties { RedirectUri = $"{context.Request.PathBase}{PipeAuthentication.SafeReturnUrl(returnUrl)}" },
            [OpenIdConnectDefaults.AuthenticationScheme]);
    }

    static public async Task<IResult> LogoutAsync(HttpContext context, PipeAuthenticationOptions options)
    {
        switch (options.Mode)
        {
            case PipeAuthenticationMode.Oidc:
                // also ends the session at the identity provider, which redirects back to the start page
                return Results.SignOut(
                    new AuthenticationProperties { RedirectUri = $"{context.Request.PathBase}/" },
                    [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
            case PipeAuthenticationMode.Forms:
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Redirect($"{context.Request.PathBase}{PipeAuthentication.LoginPath}");
            default:
                return Results.Redirect($"{context.Request.PathBase}/");
        }
    }
}
