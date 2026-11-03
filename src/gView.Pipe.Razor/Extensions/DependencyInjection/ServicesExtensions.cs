using gView.Pipe.Razor.Components;
using gView.Pipe.Razor.Editor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        // the public docs; a host that serves its own registers a PipeDocs after this
        services.TryAddSingleton(new PipeDocs());
        return services;
    }
}
