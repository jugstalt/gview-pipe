using gView.Pipe.AspNetCore.Api;
using gView.Pipe.AspNetCore.Authentication;
using gView.Pipe.AspNetCore.Webhooks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace gView.Pipe.AspNetCore.Extensions.Routing;

static public class EndpointsExtensions
{
    /// <summary>
    /// Maps <c>{prefix}/{**path}</c> (all methods) to the webhook triggers of active workflows.
    /// Requires <c>AddPipeCore()</c>.
    /// </summary>
    static public IEndpointConventionBuilder MapPipeWebhooks(this IEndpointRouteBuilder endpoints, Action<PipeWebhookEndpointOptions>? configure = null)
    {
        var options = new PipeWebhookEndpointOptions();
        configure?.Invoke(options);

        return endpoints
            .Map($"{options.Prefix.TrimEnd('/')}/{{**path}}", context => PipeWebhookEndpoint.HandleAsync(context, options))
            .WithDisplayName("gView.Pipe webhooks");
    }

    /// <summary>
    /// Maps the login form post (<c>/account/signin</c>), the OIDC challenge and the logout under <c>/account</c>.
    /// The pages <c>/account/login</c> and <c>/account/denied</c> are up to the host. Requires <c>AddPipeAuthentication()</c>.
    /// </summary>
    static public IEndpointRouteBuilder MapPipeAuthentication(this IEndpointRouteBuilder endpoints)
    {
        var login = endpoints.MapPost(PipeAuthentication.SignInPath, PipeAuthenticationEndpoints.LoginAsync)
            .AllowAnonymous()
            .DisableAntiforgery(); // validated in the handler, which reads the form itself
        if (endpoints.ServiceProvider.GetRequiredService<PipeAuthenticationOptions>().Mode == PipeAuthenticationMode.Forms)
        {
            login.RequireRateLimiting(PipeAuthentication.LoginRateLimitPolicy);
        }

        endpoints.MapGet(PipeAuthentication.ChallengePath, PipeAuthenticationEndpoints.Challenge).AllowAnonymous();
        endpoints.MapGet(PipeAuthentication.LogoutPath, PipeAuthenticationEndpoints.LogoutAsync).AllowAnonymous();

        return endpoints;
    }

    /// <summary>
    /// Maps the REST API under <c>/api/v1</c> and its OpenAPI document <c>/api/v1/openapi.json</c> (ADR 0020).
    /// The API checks its own keys and never uses the login of the UI. Requires <c>AddPipeApi()</c>.
    /// </summary>
    static public IEndpointRouteBuilder MapPipeApi(this IEndpointRouteBuilder endpoints)
    {
        PipeApiEndpoints.Map(endpoints);
        endpoints.MapOpenApi("/api/{documentName}/openapi.json").AllowAnonymous();
        return endpoints;
    }

    /// <summary>Protects endpoints (the Razor components) with the policy of <c>AddPipeAuthentication()</c>.</summary>
    static public TBuilder RequirePipeAuthorization<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.RequireAuthorization(PipeAuthentication.PolicyName);
}
