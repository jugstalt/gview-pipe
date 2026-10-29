using Microsoft.Extensions.DependencyInjection;

namespace gView.Pipe.Razor.Extensions.DependencyInjection;

static public class ServicesExtensions
{
    /// <summary>
    /// Adds the services of the gView.Pipe UI components.
    /// Requires <c>AddPipeCore()</c> (gView.Pipe.Core) and <c>AddMudServices()</c> in the host.
    /// </summary>
    static public IServiceCollection AddPipeRazor(this IServiceCollection services)
    {
        // UI services (editor state, live execution hub, …) are added with the editor
        return services;
    }
}
