using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Security;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.AspNetCore.Api;
using gView.Pipe.AspNetCore.Extensions.DependencyInjection;
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
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Tests.Api;

/// <summary>The REST API end to end (ADR 0020): keys, rights, groups, deploy, run.</summary>
public sealed class PipeApiTests : IAsyncLifetime
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
        });
        builder.Services.AddPipeApi();

        _app = builder.Build();
        _app.MapPipeApi();
        await _app.StartAsync();
        _client = _app.GetTestClient();

        var groups = _app.Services.GetRequiredService<IPipeWorkflowGroupStore>();
        await groups.SaveAsync(new PipeWorkflowGroup { Id = "billing", Name = "Billing" });
        await groups.SaveAsync(new PipeWorkflowGroup { Id = "hr", Name = "HR" });
        var workflows = _app.Services.GetRequiredService<IPipeWorkflowStore>();
        await workflows.SaveAsync(Greeter("invoice", "billing"));
        await workflows.SaveAsync(Greeter("payroll", "hr"));
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    /// <summary>Start → Set greeting from the posted name.</summary>
    static private PipeWorkflow Greeter(string id, string? groupId) => new PipeWorkflowBuilder(id, id)
        .Node("start", PipeBuiltInNodes.ManualTrigger)
        .Node("set", PipeBuiltInNodes.Set, new { fields = new { greeting = "Hello {{ $json.name }}" } })
        .Connect("start", "set")
        .Configure(w => w.GroupId = groupId)
        .Build();

    private async Task<string> KeyAsync(PipeApiRights rights, params string[] groups)
    {
        var key = PipeApiKeys.Generate();
        await _app.Services.GetRequiredService<IPipeApiKeyStore>().SaveAsync(new PipeApiKey
        {
            Name = $"test-{rights}",
            Hash = PipeApiKeys.Hash(key),
            Rights = rights,
            Groups = groups.Length > 0 ? groups.ToList() : null
        });
        return key;
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string? key, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (key is not null)
        {
            request.Headers.Add(PipeApiAuthenticator.HeaderName, key);
        }
        if (body is not null)
        {
            request.Content = new StringContent(body as string ?? PipeJson.Serialize(body), Encoding.UTF8, "application/json");
        }
        return request;
    }

    [Fact]
    public async Task Requests_need_a_valid_key_with_the_right()
    {
        var reader = await KeyAsync(PipeApiRights.Read);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/workflows")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/workflows", "pipe_unknown"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/workflows", reader))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/workflows/invoice/run", reader))).StatusCode);

        // also as bearer token
        var bearer = new HttpRequestMessage(HttpMethod.Get, "/api/v1/groups");
        bearer.Headers.Authorization = new AuthenticationHeaderValue("Bearer", reader);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(bearer)).StatusCode);
    }

    [Fact]
    public async Task Expired_keys_are_refused()
    {
        var key = PipeApiKeys.Generate();
        await _app.Services.GetRequiredService<IPipeApiKeyStore>().SaveAsync(new PipeApiKey
        {
            Name = "old", Hash = PipeApiKeys.Hash(key), Rights = PipeApiRights.Read, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/workflows", key))).StatusCode);
    }

    [Fact]
    public async Task Run_waits_for_the_output_and_records_the_key()
    {
        var runner = await KeyAsync(PipeApiRights.Run | PipeApiRights.Read);

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/workflows/invoice/run", runner,
            new { items = new[] { new { name = "Ada" } }, wait = true }));
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("success", result["status"]!.GetValue<string>());
        Assert.Equal("Hello Ada", result["items"]![0]!["greeting"]!.GetValue<string>());

        var executionId = result["executionId"]!.GetValue<string>();
        var stored = await _client.SendAsync(Request(HttpMethod.Get, $"/api/v1/executions/invoice/{executionId}", runner));
        var execution = JsonNode.Parse(await stored.Content.ReadAsStringAsync())!;
        Assert.Equal("api", execution["mode"]!.GetValue<string>());
        Assert.Equal("api:test-Read, Run", execution["startedBy"]!.GetValue<string>());
    }

    [Fact]
    public async Task Run_without_wait_answers_202_with_the_execution_id()
    {
        var runner = await KeyAsync(PipeApiRights.Run);

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/workflows/invoice/run", runner));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.False(string.IsNullOrEmpty(JsonNode.Parse(await response.Content.ReadAsStringAsync())!["executionId"]?.GetValue<string>()));
    }

    [Fact]
    public async Task Deploy_creates_validates_and_detects_concurrent_changes()
    {
        var deployer = await KeyAsync(PipeApiRights.Deploy | PipeApiRights.Read);
        var workflow = Greeter("new-one", "billing");

        var created = await _client.SendAsync(Request(HttpMethod.Put, "/api/v1/workflows/new-one", deployer, workflow));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var read = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/workflows/new-one", deployer));
        var etag = read.Headers.ETag!.Tag;

        var stale = Request(HttpMethod.Put, "/api/v1/workflows/new-one", deployer, workflow);
        stale.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await _client.SendAsync(stale)).StatusCode);

        var current = Request(HttpMethod.Put, "/api/v1/workflows/new-one", deployer, workflow);
        current.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(current)).StatusCode);

        var broken = Greeter("broken", null);
        broken.Connections.Add(new PipeConnection { SourceNodeId = "set", TargetNodeId = "start" });
        broken.Nodes.Add(new PipeWorkflowNode { Id = "x", Name = "Ghost", Type = Guid.NewGuid() });
        var invalid = await _client.SendAsync(Request(HttpMethod.Put, "/api/v1/workflows/broken", deployer, broken));
        var problem = await invalid.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Contains("cycle", problem);
        Assert.Contains("unknown node type", problem);
    }

    [Fact]
    public async Task Activating_arms_the_triggers()
    {
        var deployer = await KeyAsync(PipeApiRights.Deploy);

        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/workflows/invoice/activate", deployer))).StatusCode);
        Assert.True((await _app.Services.GetRequiredService<IPipeWorkflowStore>().GetAsync("invoice"))!.Active);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(Request(HttpMethod.Delete, "/api/v1/workflows/invoice", deployer))).StatusCode);
        Assert.Null(await _app.Services.GetRequiredService<IPipeWorkflowStore>().GetAsync("invoice"));
        Assert.False(_app.Services.GetRequiredService<IPipeTriggerManager>().IsActive("invoice"));
    }

    [Fact]
    public async Task A_key_limited_to_groups_sees_and_changes_only_their_workflows()
    {
        var billing = await KeyAsync(PipeApiRights.Read | PipeApiRights.Run | PipeApiRights.Deploy, "billing");

        var listResponse = await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/workflows", billing));
        var listText = await listResponse.Content.ReadAsStringAsync();
        Assert.True(listResponse.IsSuccessStatusCode && listText.Length > 0, $"{listResponse.StatusCode}: '{listText}'");
        var list = JsonNode.Parse(listText)!.AsArray();
        Assert.Equal(["invoice"], list.Select(w => w!["id"]!.GetValue<string>()));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/workflows/payroll", billing))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/workflows/payroll/run", billing))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(Request(HttpMethod.Put, "/api/v1/workflows/sneaky", billing, Greeter("sneaky", "hr")))).StatusCode);

        var groups = JsonNode.Parse(await (await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/groups", billing))).Content.ReadAsStringAsync())!.AsArray();
        Assert.Equal(["billing"], groups.Select(g => g!["id"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Executions_can_be_listed_and_replayed()
    {
        var key = await KeyAsync(PipeApiRights.Run | PipeApiRights.Read);
        await _client.SendAsync(Request(HttpMethod.Post, "/api/v1/workflows/invoice/run", key, new { items = new[] { new { name = "Ada" } }, wait = true }));

        var executions = JsonNode.Parse(await (await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/executions?workflowId=invoice&mode=api", key))).Content.ReadAsStringAsync())!.AsArray();
        var id = Assert.Single(executions)!["id"]!.GetValue<string>();

        var replay = await _client.SendAsync(Request(HttpMethod.Post, $"/api/v1/executions/invoice/{id}/replay", key));
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(id, JsonNode.Parse(await replay.Content.ReadAsStringAsync())!["replayOf"]!.GetValue<string>());
    }

    [Fact]
    public async Task Too_many_failed_attempts_answer_429()
    {
        HttpStatusCode last = default;
        for (var i = 0; i <= PipeApiAuthenticator.FailuresPerMinute; i++)
        {
            last = (await _client.SendAsync(Request(HttpMethod.Get, "/api/v1/workflows", "pipe_wrong"))).StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task The_openapi_document_describes_only_the_api()
    {
        var document = JsonNode.Parse(await _client.GetStringAsync("/api/v1/openapi.json"))!;
        var paths = document["paths"]!.AsObject().Select(p => p.Key).ToList();

        Assert.Contains("/api/v1/workflows/{id}/run", paths);
        Assert.All(paths, p => Assert.StartsWith("/api/v1/", p));
    }
}
