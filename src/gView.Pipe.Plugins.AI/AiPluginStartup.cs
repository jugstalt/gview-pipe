using gView.Pipe.Abstraction.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace gView.Pipe.Plugins.AI;

/// <summary>Registers the language model services behind the AI node (ADR 0041, 0042); tests replace them.</summary>
public sealed class AiPluginStartup : IPipePluginStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // the credential of the node picks one of them (ADR 0042)
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPipeLlmProvider, ClaudeLlmProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPipeLlmProvider, AzureOpenAIProvider>());
    }
}
