using gView.Pipe.Abstraction.Security;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.AspNetCore.Api;
using gView.Pipe.AspNetCore.Extensions.DependencyInjection;
using gView.Pipe.AspNetCore.Extensions.Routing;
using gView.Pipe.Cli;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.Nodes.Utility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Tests.Cli;

/// <summary>The command line client against the real API on a test server (ADR 0026).</summary>
public sealed class PipeCliTests : IAsyncLifetime
{
    private WebApplication _app = default!;
    private HttpClient _client = default!;
    private string _key = default!;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "gview-pipe-tests", Guid.NewGuid().ToString("N"));

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddPipeCore(options =>
        {
            options.ScanApplicationDirectory = false;
            options.PluginPath = null;
            options.PluginAssemblies.Add(typeof(NoOpNode).Assembly);
        });
        builder.Services.AddPipeApi();

        _app = builder.Build();
        _app.MapPipeApi();
        await _app.StartAsync();
        _client = _app.GetTestClient();

        _key = PipeApiKeys.Generate();
        await _app.Services.GetRequiredService<IPipeApiKeyStore>().SaveAsync(new PipeApiKey
        {
            Name = "cli",
            Hash = PipeApiKeys.Hash(_key),
            Rights = PipeApiRights.Read | PipeApiRights.Run | PipeApiRights.Deploy
        });
        await _app.Services.GetRequiredService<IPipeWorkflowStore>().SaveAsync(Greeter("hello"));
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    static private PipeWorkflow Greeter(string id) => new PipeWorkflowBuilder(id, id)
        .Node("start", PipeBuiltInNodes.ManualTrigger)
        .Node("set", PipeBuiltInNodes.Set, new { fields = new { greeting = "Hello {{ $json.name }}" } })
        .Connect("start", "set")
        .Build();

    static private PipeWorkflow Failing(string id) => new PipeWorkflowBuilder(id, id)
        .Node("start", PipeBuiltInNodes.ManualTrigger)
        .Node("code", PipeBuiltInNodes.Code, new { code = "throw new Error('boom');" })
        .Connect("start", "code")
        .Build();

    private async Task<(int Code, string Output, string Error)> PipeAsync(string[]? args = null, string input = "", string? key = "", params string[] more)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var all = (args ?? []).Concat(more).ToArray();
        var apiKey = key == "" ? _key : key;
        var code = await PipeCliApp.RunAsync(all, output, error, new StringReader(input), name => name == "PIPE_API_KEY" ? apiKey : null, _client);
        return (code, output.ToString(), error.ToString());
    }

    private Task<(int Code, string Output, string Error)> Pipe(params string[] args) => PipeAsync(args);

    private string WriteWorkflow(PipeWorkflow workflow, string? name = null)
    {
        var file = Path.Combine(_folder, name ?? workflow.Id + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, gView.Pipe.Abstraction.Serialization.PipeJson.Serialize(workflow));
        return file;
    }

    [Fact]
    public async Task Lists_workflows_as_a_table_and_as_json()
    {
        var table = await Pipe("workflows", "list");
        Assert.Equal(0, table.Code);
        Assert.Contains("ID", table.Output);
        Assert.Contains("hello", table.Output);

        var json = await Pipe("workflows", "list", "--json");
        Assert.Equal("hello", JsonNode.Parse(json.Output)![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Deploy_creates_then_updates_and_dry_run_changes_nothing()
    {
        var file = WriteWorkflow(Greeter("fresh"));

        var dry = await Pipe("workflows", "deploy", file, "--dry-run");
        Assert.Contains("would create fresh", dry.Output);
        Assert.Equal(404, (int)(await _client.GetAsync("/api/v1/workflows/fresh")).StatusCode);

        var created = await Pipe("workflows", "deploy", file);
        Assert.Equal(0, created.Code);
        Assert.Contains("created fresh", created.Output);

        var updated = await Pipe("workflows", "deploy", file);
        Assert.Equal(0, updated.Code);
        Assert.Contains("updated fresh", updated.Output);
    }

    [Fact]
    public async Task Deploy_takes_a_folder_and_reports_a_broken_file_with_exit_code_2()
    {
        WriteWorkflow(Greeter("one"), "flows/one.json");
        WriteWorkflow(Greeter("two"), "flows/sub/two.json");
        File.WriteAllText(Path.Combine(_folder, "flows", "broken.json"), "{ not json");

        var result = await Pipe("workflows", "deploy", Path.Combine(_folder, "flows"));

        Assert.Equal(2, result.Code);
        Assert.Contains("created one", result.Output);
        Assert.Contains("created two", result.Output);
        Assert.Contains("broken.json", result.Error);
    }

    [Fact]
    public async Task Deploy_refuses_invalid_workflows_with_the_problems_listed()
    {
        var workflow = Greeter("bad");
        workflow.Connections.Add(new PipeConnection { SourceNodeId = "set", TargetNodeId = "missing" });

        var result = await Pipe("workflows", "deploy", WriteWorkflow(workflow));

        Assert.Equal(2, result.Code);
        Assert.Contains("422", result.Error);
        Assert.Contains("missing", result.Error);
    }

    [Fact]
    public async Task Export_writes_a_file_that_deploys_again()
    {
        var target = Path.Combine(_folder, "out");
        var export = await Pipe("workflows", "export", "hello", "--out", target + Path.DirectorySeparatorChar);
        Assert.Equal(0, export.Code);

        await Pipe("workflows", "delete", "hello");
        var deploy = await Pipe("workflows", "deploy", Path.Combine(target, "hello.json"));
        Assert.Contains("created hello", deploy.Output);
    }

    [Fact]
    public async Task Activate_deactivate_and_delete()
    {
        Assert.Contains("activated hello", (await Pipe("workflows", "activate", "hello")).Output);
        Assert.Contains("deactivated hello", (await Pipe("workflows", "deactivate", "hello")).Output);
        Assert.Contains("deleted hello", (await Pipe("workflows", "delete", "hello")).Output);
        Assert.Equal(2, (await Pipe("workflows", "get", "hello")).Code);
    }

    [Fact]
    public async Task Run_with_wait_prints_the_items_and_reads_the_input_from_stdin()
    {
        var result = await PipeAsync(["run", "hello", "--wait", "--input", "-"], input: """{ "name": "Ada" }""");

        Assert.Equal(0, result.Code);
        Assert.Contains("success execution", result.Output);
        Assert.Contains("Hello Ada", result.Output);

        var executions = await Pipe("executions", "list", "--workflow", "hello", "--status", "success");
        Assert.Contains("hello", executions.Output);
    }

    [Fact]
    public async Task Run_without_wait_only_starts_and_a_failed_run_exits_with_1()
    {
        await _app.Services.GetRequiredService<IPipeWorkflowStore>().SaveAsync(Failing("broken"));

        var started = await Pipe("run", "hello");
        Assert.Equal(0, started.Code);
        Assert.Contains("started execution", started.Output);

        var failed = await Pipe("run", "broken", "--wait");
        Assert.Equal(1, failed.Code);
        Assert.Contains("error execution", failed.Output);
        Assert.Contains("boom", failed.Error);
    }

    [Fact]
    public async Task Wrong_calls_exit_with_2()
    {
        Assert.Equal(2, (await Pipe("nonsense")).Code);
        Assert.Equal(2, (await Pipe("run")).Code);
        Assert.Equal(2, (await Pipe("run", "missing", "--wait")).Code);

        var noKey = await PipeAsync(["workflows", "list"], key: null);
        Assert.Equal(2, noKey.Code);
        Assert.Contains("401", noKey.Error);
    }

    [Fact]
    public async Task Without_an_address_the_cli_says_what_is_missing()
    {
        var error = new StringWriter();

        var code = await PipeCliApp.RunAsync(["workflows", "list"], new StringWriter(), error, new StringReader(""), _ => null);

        Assert.Equal(2, code);
        Assert.Contains("PIPE_URL", error.ToString());
    }
}
