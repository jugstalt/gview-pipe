using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Plugins.Nodes.Utility;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Tests;

static public class PluginTestHost
{
    static public ServiceProvider Create(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddPipeCore(options =>
        {
            options.ScanApplicationDirectory = false;
            options.PluginPath = null;
            options.PluginAssemblies.Add(typeof(NoOpNode).Assembly);
        });
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    static public async Task<PipeExecution> RunAsync(PipeWorkflow workflow, Action<IServiceCollection>? configure = null)
    {
        using var services = Create(configure);
        var execution = await services.GetRequiredService<IPipeEngine>().ExecuteAsync(workflow);
        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        return execution;
    }

    static public List<JsonObject> Output(this PipeExecution execution, string nodeId, string port = PipePorts.Main)
        => execution.NodeRuns[nodeId].Outputs!.TryGetValue(port, out var items) ? items.Select(i => i.Json).ToList() : [];
}
