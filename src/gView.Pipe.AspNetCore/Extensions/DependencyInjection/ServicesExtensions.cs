using System.Threading.RateLimiting;
using gView.Pipe.AspNetCore.Api;
using gView.Pipe.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using gView.Pipe.Abstraction.Users;
using gView.Pipe.Abstraction.Audit;

namespace gView.Pipe.AspNetCore.Extensions.DependencyInjection;

static public class ServicesExtensions
{
    /// <summary>
    /// Registers the REST API (ADR 0020): the API key check and the OpenAPI document. Pair it with <c>app.MapPipeApi()</c>.
    /// Requires <c>AddPipeCore()</c>.
    /// </summary>
    static public IServiceCollection AddPipeApi(this IServiceCollection services)
    {
        services.TryAddSingleton<PipeApiAuthenticator>();
        services.TryAddSingleton<PipePortalRuns>();
        services.TryAddSingleton<PipeAuditRefusals>();
        services.AddOpenApi(PipeApiEndpoints.DocumentName, openApi =>
        {
            // only the API, not the webhooks or the login
            openApi.ShouldInclude = description => description.GroupName == PipeApiEndpoints.DocumentName;
            openApi.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "gView.Pipe API";
                document.Info.Description = "Send an API key as X-Pipe-Api-Key or Authorization: Bearer. Keys are created on the page API keys.";
                return Task.CompletedTask;
            });
        });
        return services;
    }

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
        services.TryAddSingleton<PipeAuditRefusals>();
        // the roles of the UI (ADR 0021); replaces the full access of AddPipeCore()
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Scoped<IPipeAuthorization, PipeClaimsAuthorization>());

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
            // forms and OIDC both end in the cookie: one place for the audit log (ADR 0022)
            cookie.Events.OnSignedIn = context => context.HttpContext.RequestServices.GetService<IPipeAuditLog>()?.WriteAsync(new PipeAuditEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Actor = context.Principal?.Identity?.Name ?? "unknown",
                Area = PipeAuditArea.Login,
                Action = "signedIn",
                Details = context.HttpContext.Connection.RemoteIpAddress?.ToString()
            }) ?? Task.CompletedTask;
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
