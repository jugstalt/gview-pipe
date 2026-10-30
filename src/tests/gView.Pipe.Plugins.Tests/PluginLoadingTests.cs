using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Abstraction.Serialization;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Loader;

namespace gView.Pipe.Plugins.Tests;

public sealed class PluginLoadingTests : IDisposable
{
    private readonly string _pluginRoot = Path.Combine(Path.GetTempPath(), "gview-pipe-tests", Guid.NewGuid().ToString("N"), "plugins");

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_pluginRoot)!, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { } // the plugin dll stays loaded for the lifetime of the test process
    }

    [Fact]
    public async Task Plugin_folder_is_loaded_into_its_own_load_context_and_shares_the_contracts()
    {
        // install the built-in plugin as if it were an external one
        var folder = Directory.CreateDirectory(Path.Combine(_pluginRoot, "gView.Pipe.Plugins")).FullName;
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "gView.Pipe.Plugins.*"))
        {
            if (!Path.GetFileName(file).StartsWith("gView.Pipe.Plugins.Tests", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
            }
        }

        var services = new ServiceCollection();
        services.AddPipeCore(options =>
        {
            options.ScanApplicationDirectory = false;
            options.PluginPath = _pluginRoot;
        });
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IPipeNodeRegistry>();
        var manual = registry.Find(Guid.Parse(PipeBuiltInNodes.ManualTrigger));

        Assert.NotNull(manual);
        Assert.Equal(12, registry.Nodes.Count);
        Assert.Equal(4, provider.GetRequiredService<IPipeCredentialTypeRegistry>().CredentialTypes.Count);
        Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(manual!.NodeType.Assembly));
        Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(typeof(IPipeNode).Assembly));

        var execution = await provider.GetRequiredService<IPipeEngine>().ExecuteAsync(SampleWorkflow());
        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
    }

    [Fact]
    public async Task Sample_workflow_of_the_shell_runs()
    {
        var execution = await PluginTestHost.RunAsync(SampleWorkflow());

        Assert.Equal(PipeNodeRunStatus.Success, execution.NodeRuns["log-high"].Status);
        Assert.Equal(PipeNodeRunStatus.Skipped, execution.NodeRuns["log-low"].Status);
        Assert.Equal("Hello Hello Pipe!", execution.Output("set")[0]["greeting"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("hello-pipe.json")]
    [InlineData("code-transform.json")]
    [InlineData("webhook-demo.json")]
    public async Task Every_sample_workflow_runs_manually(string file)
    {
        var workflow = PipeJson.Deserialize<PipeWorkflow>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", file)))!;

        var execution = await PluginTestHost.RunAsync(workflow);

        Assert.All(execution.NodeRuns.Values, run => Assert.NotEqual(PipeNodeRunStatus.Error, run.Status));
    }

    [Fact]
    public async Task Code_transform_sample_routes_and_merges_orders()
    {
        var workflow = PipeJson.Deserialize<PipeWorkflow>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "code-transform.json")))!;

        var execution = await PluginTestHost.RunAsync(workflow);

        // Linus (FI) matches no case and goes to the unconnected fallback port
        Assert.Equal([144.0, 372.0, 47.6], execution.Output("merge").Select(j => j["gross"]!.GetValue<double>()));
        Assert.Equal(3, execution.NodeRuns["log"].Logs.Count(l => l.Message.Contains("Order")));
    }

    static private PipeWorkflow SampleWorkflow()
        => PipeJson.Deserialize<PipeWorkflow>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "hello-pipe.json")))!;
}
