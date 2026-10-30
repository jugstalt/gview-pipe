using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Storage;
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
        // every id in PipeBuiltInNodes belongs to exactly one node type
        Assert.Equal(typeof(PipeBuiltInNodes).GetFields().Length, registry.Nodes.Count);
        Assert.Equal(typeof(PipeBuiltInCredentials).GetFields().Length, provider.GetRequiredService<IPipeCredentialTypeRegistry>().CredentialTypes.Count);
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
    [InlineData("item-tools.json")]
    [InlineData("sub-workflow-demo.json")]
    [InlineData("price-calculator.json")]
    [InlineData("error-alerts.json")]
    [InlineData("api-demo.json")]
    [InlineData("file-export.json")]
    public async Task Every_sample_workflow_runs_manually(string file)
    {
        // all samples are stored, so samples calling others (sub-workflows) find them
        // the folder 'files' the file samples use
        using var services = PluginTestHost.Create(options: o => o.FileRoots["files"] = Path.Combine(_pluginRoot, "..", "files"));
        var store = services.GetRequiredService<IPipeWorkflowStore>();
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Samples"), "*.json"))
        {
            await store.SaveAsync(PipeJson.Deserialize<PipeWorkflow>(File.ReadAllText(path))!);
        }

        var execution = await services.GetRequiredService<IPipeWorkflowRunner>().RunAsync(Path.GetFileNameWithoutExtension(file));

        Assert.Equal(PipeExecutionStatus.Success, execution!.Status);
        Assert.All(execution.NodeRuns.Values, run => Assert.NotEqual(PipeNodeRunStatus.Error, run.Status));
    }

    [Fact]
    public void Sample_list_covers_every_sample_file()
    {
        var tested = typeof(PluginLoadingTests).GetMethod(nameof(Every_sample_workflow_runs_manually))!
            .GetCustomAttributes(typeof(InlineDataAttribute), false).Cast<InlineDataAttribute>()
            .Select(a => (string)a.Data[0]!);

        Assert.Equal(
            Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Samples"), "*.json").Select(Path.GetFileName).Order(),
            tested.Order());
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

    [Fact]
    public async Task Item_tools_sample_collects_the_most_expensive_unique_skus()
    {
        var workflow = PipeJson.Deserialize<PipeWorkflow>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "item-tools.json")))!;

        var execution = await PluginTestHost.RunAsync(workflow);

        Assert.Equal("""{"sku":["lamp","ink","pad"]}""", Assert.Single(execution.Output("aggregate")).ToJsonString());
    }

    static private PipeWorkflow SampleWorkflow()
        => PipeJson.Deserialize<PipeWorkflow>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "hello-pipe.json")))!;
}
