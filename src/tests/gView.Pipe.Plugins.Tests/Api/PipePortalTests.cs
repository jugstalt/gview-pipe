using gView.Pipe.Abstraction.Security;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Storage;
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
using System.Text;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Tests.Api;

/// <summary>The portal endpoints end to end (ADR 0027): published workflows, forms, delegation, own runs.</summary>
public sealed class PipePortalTests : IAsyncLifetime
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

        await _app.Services.GetRequiredService<IPipeWorkflowGroupStore>().SaveAsync(new PipeWorkflowGroup { Id = "office", Name = "Office" });
        var workflows = _app.Services.GetRequiredService<IPipeWorkflowStore>();
        await workflows.SaveAsync(Greeter("greeter", published: true));
        await workflows.SaveAsync(Greeter("hidden", published: false));
        await workflows.SaveAsync(Failing("failing"));
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    /// <summary>Form (name, age) → Set a greeting → Result text.</summary>
    static private PipeWorkflow Greeter(string id, bool published) => new PipeWorkflowBuilder(id, id)
        .Node("form", PipeBuiltInNodes.FormTrigger, new
        {
            fields = new object[]
            {
                new { name = "name", label = "Your name", type = "text", required = true },
                new { name = "age", type = "number" },
                new { name = "size", type = "choice", options = new[] { "small", "large" } },
                new { name = "plate", type = "text", placeholder = "WL-123", pattern = @"[A-Z]{1,2}-\d{1,5}", patternMessage = "Like WL-123" }
            }
        })
        .Node("set", PipeBuiltInNodes.Set, new { fields = new { greeting = "Hello {{ $json.name }}" } })
        .Node("result", PipeBuiltInNodes.Result, new { kind = "text", title = "Greeting", content = "{{ $json.greeting }}" })
        .Connect("form", "set")
        .Connect("set", "result")
        .Configure(w =>
        {
            w.GroupId = "office";
            w.Settings.Portal = new PipeWorkflowPortalSettings { Published = published };
        })
        .Build();

    /// <summary>Throws in a Code node; the Result node has an own error message.</summary>
    static private PipeWorkflow Failing(string id) => new PipeWorkflowBuilder(id, id)
        .Node("form", PipeBuiltInNodes.FormTrigger, new { fields = Array.Empty<object>() })
        .Node("code", PipeBuiltInNodes.Code, new { code = "throw new Error('secret connection string')" })
        .Node("result", PipeBuiltInNodes.Result, new { kind = "text", errorMessage = "The import failed; call the help desk." })
        .Connect("form", "code")
        .Connect("code", "result")
        .Configure(w => w.Settings.Portal = new PipeWorkflowPortalSettings { Published = true })
        .Build();

    private async Task<string> KeyAsync(PipeApiRights rights)
    {
        var key = PipeApiKeys.Generate();
        await _app.Services.GetRequiredService<IPipeApiKeyStore>().SaveAsync(new PipeApiKey
        {
            Name = $"test-{rights}",
            Hash = PipeApiKeys.Hash(key),
            Rights = rights
        });
        return key;
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string key, string? user, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(PipeApiAuthenticator.HeaderName, key);
        if (user is not null)
        {
            request.Headers.Add(PipePortalEndpoints.UserHeader, user);
        }
        if (body is not null)
        {
            request.Content = new StringContent(PipeJson.Serialize(body), Encoding.UTF8, "application/json");
        }
        return request;
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, string key, string? user, HttpStatusCode expected, object? body = null)
    {
        var response = await _client.SendAsync(Request(method, path, key, user, body));
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"{method} {path}: {(int)response.StatusCode} {text}");
        return text.Length > 0 ? JsonNode.Parse(text)! : new JsonObject();
    }

    [Fact]
    public async Task Portal_keys_see_only_published_workflows_with_their_forms()
    {
        var key = await KeyAsync(PipeApiRights.Portal);

        var list = (JsonArray)await SendAsync(HttpMethod.Get, "/api/v1/portal/workflows", key, "anna", HttpStatusCode.OK);

        Assert.Contains(list, w => (string?)w!["id"] == "greeter");
        Assert.DoesNotContain(list, w => (string?)w!["id"] == "hidden");
        var greeter = list.First(w => (string?)w!["id"] == "greeter")!;
        Assert.Equal("Office", (string?)greeter["category"]);
        Assert.Equal(["name", "age", "size", "plate"], ((JsonArray)greeter["form"]!).Select(f => (string?)f!["name"]).ToArray());
        Assert.True((bool)greeter["form"]![0]!["required"]!);
        Assert.Equal("WL-123", (string?)greeter["form"]![3]!["placeholder"]);
        Assert.Equal("Like WL-123", (string?)greeter["form"]![3]!["patternMessage"]);
    }

    [Fact]
    public async Task Portal_endpoints_need_the_portal_right_and_a_user()
    {
        var portal = await KeyAsync(PipeApiRights.Portal);
        var all = await KeyAsync(PipeApiRights.Read | PipeApiRights.Run | PipeApiRights.Deploy);

        await SendAsync(HttpMethod.Get, "/api/v1/portal/workflows", all, "anna", HttpStatusCode.Forbidden);
        await SendAsync(HttpMethod.Get, "/api/v1/portal/workflows", portal, null, HttpStatusCode.BadRequest);
        // a portal key reaches nothing else
        await SendAsync(HttpMethod.Get, "/api/v1/workflows", portal, "anna", HttpStatusCode.Forbidden);
        await SendAsync(HttpMethod.Post, "/api/v1/workflows/greeter/run", portal, "anna", HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_run_checks_the_form_and_returns_the_result()
    {
        var key = await KeyAsync(PipeApiRights.Portal);

        var problems = await SendAsync(HttpMethod.Post, "/api/v1/portal/workflows/greeter/run", key, "anna", HttpStatusCode.UnprocessableEntity,
            new { values = new { age = "old", size = "huge" } });
        var details = problems["details"]!;
        Assert.Equal("Required", (string?)details["name"]);
        Assert.Equal("Not a number", (string?)details["age"]);
        Assert.Equal("Not one of the choices", (string?)details["size"]);

        var done = await SendAsync(HttpMethod.Post, "/api/v1/portal/workflows/greeter/run", key, "anna", HttpStatusCode.OK,
            new { values = new { name = "Anna", age = "41", extra = "ignored" } });
        Assert.Equal("success", (string?)done["status"]);
        Assert.Equal("Hello Anna", (string?)done["result"]!["text"]);
        Assert.Equal("Greeting", (string?)done["result"]!["title"]);
    }

    [Fact]
    public async Task A_pattern_must_match_the_whole_text()
    {
        var key = await KeyAsync(PipeApiRights.Portal);

        var wrong = await SendAsync(HttpMethod.Post, "/api/v1/portal/workflows/greeter/run", key, "anna", HttpStatusCode.UnprocessableEntity,
            new { values = new { name = "Anna", plate = "xx WL-123 yy" } });
        Assert.Equal("Like WL-123", (string?)wrong["details"]!["plate"]);

        await SendAsync(HttpMethod.Post, "/api/v1/portal/workflows/greeter/run", key, "anna", HttpStatusCode.OK,
            new { values = new { name = "Anna", plate = "WL-123" } });
    }

    [Fact]
    public async Task Unpublished_workflows_cannot_be_started()
    {
        var key = await KeyAsync(PipeApiRights.Portal);

        await SendAsync(HttpMethod.Post, "/api/v1/portal/workflows/hidden/run", key, "anna", HttpStatusCode.NotFound, new { values = new { name = "Anna" } });
    }

    [Fact]
    public async Task Users_see_only_their_own_runs()
    {
        var key = await KeyAsync(PipeApiRights.Portal);
        var done = await SendAsync(HttpMethod.Post, "/api/v1/portal/workflows/greeter/run", key, "anna", HttpStatusCode.OK,
            new { values = new { name = "Anna" } });
        var id = (string)done["executionId"]!;

        var own = (JsonArray)await SendAsync(HttpMethod.Get, "/api/v1/portal/executions", key, "anna", HttpStatusCode.OK);
        var other = (JsonArray)await SendAsync(HttpMethod.Get, "/api/v1/portal/executions", key, "ben", HttpStatusCode.OK);

        Assert.Contains(own, e => (string?)e!["executionId"] == id);
        Assert.Empty(other);
        await SendAsync(HttpMethod.Get, $"/api/v1/portal/executions/greeter/{id}", key, "anna", HttpStatusCode.OK);
        await SendAsync(HttpMethod.Get, $"/api/v1/portal/executions/greeter/{id}", key, "ben", HttpStatusCode.NotFound);
        await SendAsync(HttpMethod.Post, $"/api/v1/portal/executions/greeter/{id}/cancel", key, "ben", HttpStatusCode.NotFound);

        // the execution says who started it
        var stored = await _app.Services.GetRequiredService<IPipeExecutionStore>().GetAsync("greeter", id);
        Assert.Equal("portal:anna", stored!.StartedBy);
    }

    [Fact]
    public async Task A_failed_run_shows_the_message_of_the_author_not_the_error()
    {
        var key = await KeyAsync(PipeApiRights.Portal);

        var failed = await SendAsync(HttpMethod.Post, "/api/v1/portal/workflows/failing/run", key, "anna", HttpStatusCode.OK, new { values = new { } });

        Assert.Equal("error", (string?)failed["status"]);
        Assert.Equal("The import failed; call the help desk.", (string?)failed["error"]);
        Assert.DoesNotContain("secret", failed.ToJsonString());
    }
}
