using gView.Pipe.AspNetCore.Webhooks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

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
}
