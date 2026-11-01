using System.Threading.RateLimiting;
using gView.Pipe.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

namespace gView.Pipe.AspNetCore.Extensions.DependencyInjection;

static public class ServicesExtensions
{
    /// <summary>
    /// Registers the login of the UI (ADR 0014): none, a form with configured users, or OpenID Connect.
    /// Pair it with <c>app.UsePipeAuthentication()</c>, <c>app.MapPipeAuthentication()</c> and
    /// <c>RequirePipeAuthorization()</c> on the UI endpoints.
    /// </summary>
    static public IServiceCollection AddPipeAuthentication(this IServiceCollection services, Action<PipeAuthenticationOptions> configure)
    {
        var options = new PipeAuthenticationOptions();
        configure(options);
        services.AddSingleton(options);

        if (options.UseForwardedHeaders)
        {
            services.Configure<ForwardedHeadersOptions>(forwarded =>
            {
                forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
                // the proxy is whatever the admin put in front; it is switched on on purpose
                forwarded.KnownIPNetworks.Clear();
                forwarded.KnownProxies.Clear();
            });
        }

        // always registered: the UI endpoints carry the policy, which lets everyone through in the mode None
        services.AddAuthorization(authorization => authorization.AddPolicy(PipeAuthentication.PolicyName, policy =>
            policy.RequireAssertion(context => PipeAuthentication.IsAllowed(context.User, options))));
        services.AddCascadingAuthenticationState();

        if (options.Mode == PipeAuthenticationMode.None)
        {
            return services;
        }

        var authentication = options.Mode == PipeAuthenticationMode.Oidc
            ? services.AddAuthentication(auth =>
            {
                auth.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                auth.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
                auth.DefaultForbidScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            : services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme);

        authentication.AddCookie(cookie =>
        {
            cookie.Cookie.Name = "gview-pipe.auth";
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.ExpireTimeSpan = TimeSpan.FromMinutes(Math.Max(1, options.SessionMinutes));
            cookie.SlidingExpiration = true;
            cookie.LoginPath = PipeAuthentication.LoginPath;
            cookie.AccessDeniedPath = PipeAuthentication.DeniedPath;
        });

        if (options.Mode == PipeAuthenticationMode.Oidc)
        {
            var oidc = options.Oidc;
            if (string.IsNullOrWhiteSpace(oidc.Authority) || string.IsNullOrWhiteSpace(oidc.ClientId))
            {
                throw new InvalidOperationException("Pipe:Auth:Oidc needs an Authority and a ClientId.");
            }

            authentication.AddOpenIdConnect(openId =>
            {
                openId.Authority = oidc.Authority;
                openId.ClientId = oidc.ClientId;
                openId.ClientSecret = string.IsNullOrEmpty(oidc.ClientSecret) ? null : oidc.ClientSecret;
                openId.ResponseType = "code";
                openId.UsePkce = true;
                openId.RequireHttpsMetadata = oidc.RequireHttpsMetadata;
                // the id token is the hint for the logout at the provider
                openId.SaveTokens = true;
                openId.GetClaimsFromUserInfoEndpoint = true;
                openId.MapInboundClaims = false;
                openId.Scope.Clear();
                foreach (var scope in oidc.Scopes.Count > 0 ? oidc.Scopes : ["openid", "profile", "role"])
                {
                    openId.Scope.Add(scope);
                }
                // userinfo may return the roles as a JSON array; this makes one claim per role
                openId.ClaimActions.MapJsonKey(oidc.RoleClaimType, oidc.RoleClaimType);
                openId.TokenValidationParameters.NameClaimType = oidc.NameClaimType;
                openId.TokenValidationParameters.RoleClaimType = oidc.RoleClaimType;
            });
        }
        else
        {
            services.AddRateLimiter(limiter =>
            {
                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                limiter.AddPolicy(PipeAuthentication.LoginRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, options.LoginAttemptsPerMinute),
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
            });
        }

        return services;
    }
}
