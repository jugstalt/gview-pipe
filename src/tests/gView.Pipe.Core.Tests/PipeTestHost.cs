using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Core.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace gView.Pipe.Core.Tests;

static public class PipeTestHost
{
    static public ServiceProvider Create(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddPipeCore(options =>
        {
            options.ScanApplicationDirectory = false;
            options.PluginPath = null;
            options.PluginAssemblies.Add(typeof(PipeTestHost).Assembly);
        });
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    static public IPipeEngine Engine(this ServiceProvider services) => services.GetRequiredService<IPipeEngine>();
}
