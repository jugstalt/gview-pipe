using gView.Pipe.Razor.Editor;
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
        // per circuit: the in-memory fallback of the clipboard belongs to one user session
        services.AddScoped<PipeEditorClipboard>();
        return services;
    }
}
