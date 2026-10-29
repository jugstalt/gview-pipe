using Microsoft.Extensions.DependencyInjection;

namespace gView.Pipe.Abstraction.Plugins;

/// <summary>
/// Optional entry point of a plugin assembly to register its own services.
/// Needs a public parameterless constructor; called once while the host configures its services.
/// </summary>
public interface IPipePluginStartup
{
    void ConfigureServices(IServiceCollection services);
}
