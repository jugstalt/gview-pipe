using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Abstraction.Variables;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.AspNetCore.Extensions.Routing;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.Nodes.Utility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Tests.Nodes;

/// <summary>Environments and variables (ADR 0013): resolution, $vars, sub-workflows and webhooks.</summary>
public sealed class EnvironmentTests : IDisposable
{
    private readonly ServiceProvider _services = PluginTestHost.Create(options: Instance);

    public void Dispose() => _services.Dispose();

    /// <summary>A PROD instance with DB_SERVER for DEFAULT and PROD, and a secret DB_PASSWORD.</summary>
    static private void Instance(gView.Pipe.Core.Options.PipeCoreOptions options)
    {
        options.Environments.AddRange(["test", "STAGE", "PROD"]);
        options.Environment = "PROD";
        options.Variables["DB_SERVER"] = new() { ["DEFAULT"] = "db-default", ["PROD"] = "db-prod" };
        options.Variables["DB_PASSWORD"] = new() { ["DEFAULT"] = "s3cr3t-pw" };
        options.SecretVariables.Add("db_password");
    }

    private IPipeWorkflowRunner Runner => _services.GetRequiredService<IPipeWorkflowRunner>();
    private IPipeEnvironments Environments => _services.GetRequiredService<IPipeEnvironments>();

    static private PipeVariable Variable(string name, params (string Environment, string Value)[] values) => new()
    {
        Name = name,
        Values = values.ToDictionary(v => v.Environment, v => v.Value)
    };

    static private PipeWorkflow Reader(string id = "reader") => new PipeWorkflowBuilder("Reader", id)
        .Node("start", PipeBuiltInNodes.ManualTrigger)
        .Node("set", PipeBuiltInNodes.Set, new { fields = new { environment = "{{ $environment }}", server = "{{ $vars.DB_SERVER }}" } })
        .Connect("start", "set")
        .Build();

    [Fact]
    public void Environments_are_upper_case_with_default_first()
    {
        Assert.Equal(["DEFAULT", "TEST", "STAGE", "PROD"], Environments.All);
        Assert.Equal("PROD", Environments.Instance);
    }

    [Fact]
    public async Task Effective_environment_comes_from_workflow_then_group_then_instance()
    {
        var group = new PipeWorkflowGroup { Name = "Imports", Environment = "STAGE" };
        await _services.GetRequiredService<IPipeWorkflowGroupStore>().SaveAsync(group);

        var workflow = Reader();
        Assert.Equal(new PipeEffectiveEnvironment("PROD", PipeEnvironmentSource.Instance), await Environments.ResolveAsync(workflow));

        workflow.GroupId = group.Id;
        Assert.Equal(new PipeEffectiveEnvironment("STAGE", PipeEnvironmentSource.Group, "Imports"), await Environments.ResolveAsync(workflow));

        workflow.Environment = "test";
        Assert.Equal(new PipeEffectiveEnvironment("TEST", PipeEnvironmentSource.Workflow), await Environments.ResolveAsync(workflow));
    }

    [Fact]
    public async Task A_value_for_the_environment_on_a_higher_level_beats_a_default_value_on_a_lower_one()
    {
        var group = new PipeWorkflowGroup
        {
            Name = "Group",
            Variables = [Variable("DB_SERVER", ("TEST", "db-group-test")), Variable("SCHEMA", ("DEFAULT", "group-schema"))]
        };
        await _services.GetRequiredService<IPipeWorkflowGroupStore>().SaveAsync(group);
        var workflow = Reader();
        workflow.GroupId = group.Id;
        workflow.Variables = [Variable("DB_SERVER", ("DEFAULT", "db-workflow")), Variable("SCHEMA", ("DEFAULT", "workflow-schema"))];

        // PROD: the instance's PROD value wins over the workflow's DEFAULT value
        var prod = await Environments.ResolveVariablesAsync(workflow, "PROD");
        Assert.Equal("db-prod", prod.Values["DB_SERVER"]);
        Assert.Equal("workflow-schema", prod.Values["SCHEMA"]);

        // TEST: only the group has a TEST value
        Assert.Equal("db-group-test", (await Environments.ResolveVariablesAsync(workflow, "TEST")).Values["DB_SERVER"]);

        // STAGE: nobody has one, the lowest DEFAULT value wins
        Assert.Equal("db-workflow", (await Environments.ResolveVariablesAsync(workflow, "STAGE")).Values["DB_SERVER"]);

        Assert.Contains("DB_PASSWORD", prod.Secrets);
    }

    [Fact]
    public async Task Instance_variables_of_the_store_override_the_configuration_per_environment()
    {
        await _services.GetRequiredService<IPipeVariableStore>().SaveAsync([Variable("db_server", ("PROD", "db-prod-new"))]);

        var variable = Assert.Single(await Environments.GetInstanceVariablesAsync(), v => v.Name == "DB_SERVER");
        Assert.Equal("db-prod-new", variable.Values["PROD"]);
        Assert.Equal("db-default", variable.Values["DEFAULT"]);
    }

    [Fact]
    public async Task Executions_run_in_the_effective_or_the_requested_environment()
    {
        var saved = await Runner.RunAsync(Reader());
        Assert.Equal("PROD", saved!.Environment);
        Assert.Equal("db-prod", saved.Output("set")[0]["server"]!.GetValue<string>());

        // like a test run from the editor
        var test = await Runner.RunAsync(Reader(), new PipeExecutionRequest { Environment = "test" });
        Assert.Equal("TEST", test!.Environment);
        Assert.Equal("TEST", test.Output("set")[0]["environment"]!.GetValue<string>());
        Assert.Equal("db-default", test.Output("set")[0]["server"]!.GetValue<string>());
    }

    [Fact]
    public async Task Reading_an_undefined_variable_fails_with_its_name_and_environment()
    {
        var workflow = new PipeWorkflowBuilder("Typo")
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("set", PipeBuiltInNodes.Set, new { fields = new { server = "{{ $vars.DB_SERVR }}", known = "{{ 'DB_SERVER' in $vars }}" } })
            .Connect("start", "set")
            .Build();

        var execution = await Runner.RunAsync(workflow);

        Assert.Equal(PipeExecutionStatus.Error, execution!.Status);
        Assert.Contains("Variable 'DB_SERVR' is not defined in environment PROD", execution.Error);
    }

    [Fact]
    public async Task Secret_values_are_masked_in_logs_and_errors()
    {
        var workflow = new PipeWorkflowBuilder("Leak")
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("log", PipeBuiltInNodes.Log, new { message = "password is {{ $vars.DB_PASSWORD }}" })
            .Node("fail", PipeBuiltInNodes.Code, new { code = "throw new Error('login failed with ' + $vars.DB_PASSWORD);" })
            .Connect("start", "log")
            .Connect("log", "fail")
            .Build();

        var execution = await Runner.RunAsync(workflow);

        Assert.Equal(PipeExecutionStatus.Error, execution!.Status);
        Assert.Contains(execution.NodeRuns["log"].Logs, l => l.Message.Contains("password is ***"));
        Assert.DoesNotContain("s3cr3t-pw", PipeJsonText(execution));
    }

    static private string PipeJsonText(PipeExecution execution)
        => string.Join("\n", execution.NodeRuns.Values.SelectMany(r => r.Logs.Select(l => l.Message)).Append(execution.Error ?? "")
            .Concat(execution.NodeRuns.Values.Select(r => r.Error ?? "")));

    [Fact]
    public async Task Sub_workflows_inherit_the_environment_of_the_caller()
    {
        await _services.GetRequiredService<IPipeWorkflowStore>().SaveAsync(new PipeWorkflowBuilder("Child", "child")
            .Node("in", PipeBuiltInNodes.ExecuteWorkflowTrigger)
            .Node("set", PipeBuiltInNodes.Set, new { fields = new { environment = "{{ $environment }}" } })
            .Connect("in", "set")
            .Configure(w => w.Environment = "PROD")
            .Build());
        var caller = new PipeWorkflowBuilder("Caller")
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("call", PipeBuiltInNodes.ExecuteWorkflow, new { workflowId = "child" })
            .Connect("start", "call")
            .Build();

        var execution = await Runner.RunAsync(caller, new PipeExecutionRequest { Environment = "TEST" });

        Assert.Equal(PipeExecutionStatus.Success, execution!.Status);
        Assert.Equal("TEST", execution.Output("call")[0]["environment"]!.GetValue<string>());
        Assert.Equal("TEST", (await _services.GetRequiredService<IPipeExecutionStore>().ListAsync("child")).Single().Environment);
    }
}

/// <summary>The header X-Pipe-Environment of webhook calls.</summary>
public sealed class WebhookEnvironmentTests : IAsyncLifetime
{
    private WebApplication _app = default!;
    private HttpClient _client = default!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddPipeCore(options =>
        {
            options.ScanApplicationDirectory = false;
            options.PluginPath = null;
            options.PluginAssemblies.Add(typeof(NoOpNode).Assembly);
            options.Environments.AddRange(["TEST", "PROD"]);
            options.Environment = "PROD";
        });

        _app = builder.Build();
        _app.MapPipeWebhooks();
        await _app.StartAsync();
        _client = _app.GetTestClient();

        var workflow = new PipeWorkflowBuilder("Hook")
            .Node("hook", PipeBuiltInNodes.WebhookTrigger, new { path = "env", responseMode = "whenFinished" })
            .Node("set", PipeBuiltInNodes.Set, new { fields = new { environment = "{{ $environment }}" } })
            .Connect("hook", "set")
            .Configure(w =>
            {
                w.Active = true;
                w.Settings.WebhookEnvironments = ["TEST"];
            })
            .Build();
        await _app.Services.GetRequiredService<IPipeWorkflowStore>().SaveAsync(workflow);
        Assert.Empty(await _app.Services.GetRequiredService<IPipeTriggerManager>().ActivateAsync(workflow));
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<(HttpStatusCode Status, JsonNode? Body)> CallAsync(string? environment)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhook/env");
        if (environment is not null)
        {
            request.Headers.Add("X-Pipe-Environment", environment);
        }
        var response = await _client.SendAsync(request);
        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Without_the_header_the_saved_environment_applies()
    {
        var (status, body) = await CallAsync(null);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("PROD", body!["environment"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_allowed_environment_can_be_chosen()
    {
        var (status, body) = await CallAsync("test");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("TEST", body!["environment"]!.GetValue<string>());
    }

    [Fact]
    public async Task Another_environment_is_rejected_not_ignored()
    {
        var (status, body) = await CallAsync("DEFAULT");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("DEFAULT", body!["error"]!.GetValue<string>());
    }
}
