using gView.Pipe.Abstraction.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace gView.Pipe.Plugins.AzureStorage;

/// <summary>Registers the connector of the Azure Blob node and trigger (ADR 0040); tests replace it.</summary>
public sealed class AzureStoragePluginStartup : IPipePluginStartup
{
    public void ConfigureServices(IServiceCollection services)
        => services.TryAddSingleton<IPipeBlobConnector, AzureBlobConnector>();
}
