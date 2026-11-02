using gView.Pipe.Abstraction.Execution;
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
using Microsoft.Extensions.Configuration;
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
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Pipe:Portal:NotifyWorkflowId"] = "notify",
            ["Pipe:Portal:PublicUrl"] = "https://portal.example.com/"
        });

        _app = builder.Build();
        _app.MapPipeApi();
        await _app.StartAsync();
        _client = _app.GetTestClient();

        await _app.Services.GetRequiredService<IPipeWorkflowGroupStore>().SaveAsync(new PipeWorkflowGroup { Id = "office", Name = "Office" });
        var workflows = _app.Services.GetRequiredService<IPipeWorkflowStore>();
        await workflows.SaveAsync(Greeter("greeter", published: true));
        await workflows.SaveAsync(Greeter("hidden", published: false));
        await workflows.SaveAsync(Failing("failing"));
        // portal roles (ADR 0030): the group finance, a workflow in it, and one of the group that names hr itself
        await _app.Services.GetRequiredService<IPipeWorkflowGroupStore>().SaveAsync(new PipeWorkflowGroup { Id = "finance", Name = "Finance", PortalRoles = ["finance"] });
        await workflows.SaveAsync(Restricted("payroll", roles: null));
        await workflows.SaveAsync(Restricted("hr-only", roles: ["hr"]));
        // the mail workflow of the admin: receives { to, user, workflow, execution }
        await workflows.SaveAsync(new PipeWorkflowBuilder("Notify", "notify")
            .Node("start", PipeBuiltInNodes.ExecuteWorkflowTrigger)
            .Node("noop", PipeBuiltInNodes.NoOp)
            .Connect("start", "noop")
            .Build());
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
                new { name = "secret", type = "password" },
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

    /// <summary>A published workflow in the group finance, with roles of its own or those of the group.</summary>
    static private PipeWorkflow Restricted(string id, List<string>? roles) => new PipeWorkflowBuilder(id, id)
        .Node("form", PipeBuiltInNodes.FormTrigger, new { fields = Array.Empty<object>() })
        .Node("result", PipeBuiltInNodes.Result, new { kind = "text", content = "done" })
        .Connect("form", "result")
        .Configure(w =>
        {
            w.GroupId = "finance";
            w.Settings.Portal = new PipeWorkflowPortalSettings { Published = true, Roles = roles };
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

    private HttpRequestMessage Request(HttpMethod method, string path, string key, string? user, object? body = null, string? roles = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(PipeApiAuthenticator.HeaderName, key);
        if (user is not null)
        {
            request.Headers.Add(PipePortalEndpoints.UserHeader, user);
        }
        if (roles is not null)
        {
            request.Headers.Add(PipePortalEndpoints.RolesHeader, roles);
        }
        if (body is not null)
        {
            request.Content = new StringContent(PipeJson.Serialize(body), Encoding.UTF8, "application/json");
        }
        return request;
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, string key, string? user, HttpStatusCode expected, object? body = null, string? roles = null)
    {
        var response = await _client.SendAsync(Request(method, path, key, user, body, roles));
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
        Assert.Equal(["name", "age", "size", "secret", "plate"], ((JsonArray)greeter["form"]!).Select(f => (string?)f!["name"]).ToArray());
        Assert.True((bool)greeter["form"]![0]!["required"]!);
        Assert.Equal("WL-123", (string?)greeter["form"]![4]!["placeholder"]);
        Assert.Equal("Like WL-123", (string?)greeter["form"]![4]!["patternMessage"]);
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

    private async Task<IReadOnlyList<PipeExecutionSummary>> NotificationsAsync(int expected)
    {
        var store = _app.Services.GetRequiredService<IPipeExecutionStore>();
        for (var i = 0; i < 50; i++)
        {
            var found = await store.QueryAsync(new PipeExecutionQuery { WorkflowIds = ["notify"] });
            if (found.Count >= expected)
            {
                return found;
            }
            await Task.Delay(100);
        }
        return await store.QueryAsync(new PipeExecutionQuery { WorkflowIds = ["notify"] });
    }

    [Fact]
    public async Task The_info_says_whether_a_mail_can_be_sent()
    {
        var key = await KeyAsync(PipeApiRights.Portal);

        var info = await SendAsync(HttpMethod.Get, "/api/v1/portal/info", key, "anna", HttpStatusCode.OK);
        Assert.True((bool)info["mailWhenDone"]!);
        Assert.Equal(30, (int)info["historyDays"]!);

        _app.Configuration["Pipe:Portal:NotifyWorkflowId"] = "";
        info = await SendAsync(HttpMethod.Get, "/api/v1/portal/info", key, "anna", HttpStatusCode.OK);
        Assert.False((bool)info["mailWhenDone"]!);
        // without a notification workflow a mail is refused
        await SendAsync(HttpMethod.Post, "/api/v1/portal/workflows/greeter/run", key, "anna", HttpStatusCode.BadRequest,
            new { values = new { name = "Anna" }, notifyEmail = "anna@example.com" });
    }

    [Fact]
    public async Task A_background_run_starts_the_notification_workflow_when_it_has_ended()
    {
        var key = await KeyAsync(PipeApiRights.Portal);

        var started = await SendAsync(HttpMethod.Post, "/api/v1/portal/workflows/greeter/run", key, "anna", HttpStatusCode.Accepted,
            new { values = new { name = "Anna" }, mode = "background", notifyEmail = "anna@example.com" });

        var notification = Assert.Single(await NotificationsAsync(1));
        var stored = await _app.Services.GetRequiredService<IPipeExecutionStore>().GetAsync("notify", notification.Id);
        var item = stored!.NodeRuns["start"].Outputs!["main"][0].Json;
        Assert.Equal("anna@example.com", (string?)item["to"]);
        Assert.Equal("anna", (string?)item["user"]);
        Assert.Equal("greeter", (string?)item["workflow"]!["name"]);
        Assert.Equal("success", (string?)item["execution"]!["status"]);
        Assert.Equal($"https://portal.example.com/jobs/greeter/{(string?)started["executionId"]}", (string?)item["execution"]!["url"]);
        // never the result of the run
        Assert.DoesNotContain("Hello", item.ToJsonString());
        Assert.Equal("portal-mail", stored.StartedBy);
    }

    [Fact]
    public async Task No_mail_when_the_user_got_the_result_while_waiting()
    {
        var key = await KeyAsync(PipeApiRights.Portal);

        await SendAsync(HttpMethod.Post, "/api/v1/portal/workflows/greeter/run", key, "anna", HttpStatusCode.OK,
            new { values = new { name = "Anna" }, mode = "wait", notifyEmail = "anna@example.com" });

        await Task.Delay(300);
        Assert.Empty(await _app.Services.GetRequiredService<IPipeExecutionStore>().QueryAsync(new PipeExecutionQuery { WorkflowIds = ["notify"] }));
    }

    [Fact]
    public async Task A_wrong_address_for_the_mail_is_refused()
    {
        var key = await KeyAsync(PipeApiRights.Portal);

        await SendAsync(HttpMethod.Post, "/api/v1/portal/workflows/greeter/run", key, "anna", HttpStatusCode.BadRequest,
            new { values = new { name = "Anna" }, notifyEmail = "Anna <anna@example.com>" });
        await SendAsync(HttpMethod.Post, "/api/v1/portal/workflows/greeter/run", key, "anna", HttpStatusCode.BadRequest,
            new { values = new { name = "Anna" }, notifyEmail = "not an address" });
    }

    [Fact]
    public async Task A_run_shows_the_values_without_passwords_for_running_it_again()
    {
        var key = await KeyAsync(PipeApiRights.Portal);
        var done = await SendAsync(HttpMethod.Post, "/api/v1/portal/workflows/greeter/run", key, "anna", HttpStatusCode.OK,
            new { values = new { name = "Anna", age = "41", secret = "hunter2" } });

        var detail = await SendAsync(HttpMethod.Get, $"/api/v1/portal/executions/greeter/{(string)done["executionId"]!}", key, "anna", HttpStatusCode.OK);

        var values = detail["values"]!;
        Assert.Equal("Anna", (string?)values["name"]);
        Assert.Equal(41, (int?)values["age"]);
        Assert.Null(values["secret"]);
        Assert.DoesNotContain("hunter2", detail.ToJsonString());
    }

    [Fact]
    public async Task After_the_days_of_the_history_only_the_status_is_shown()
    {
        var key = await KeyAsync(PipeApiRights.Portal);
        var done = await SendAsync(HttpMethod.Post, "/api/v1/portal/workflows/greeter/run", key, "anna", HttpStatusCode.OK,
            new { values = new { name = "Anna" } });
        Assert.NotNull((await SendAsync(HttpMethod.Get, $"/api/v1/portal/executions/greeter/{(string)done["executionId"]!}", key, "anna", HttpStatusCode.OK))["result"]);

        // the same run, 40 days ago (a run of the last minute is still in memory, so it gets an id of its own)
        var store = _app.Services.GetRequiredService<IPipeExecutionStore>();
        var execution = PipeJson.Clone((await store.GetAsync("greeter", (string)done["executionId"]!))!);
        execution.Id = Guid.NewGuid().ToString("N");
        execution.StartedAt = DateTimeOffset.UtcNow.AddDays(-40).AddSeconds(-5);
        execution.FinishedAt = DateTimeOffset.UtcNow.AddDays(-40);
        await store.SaveAsync(execution);
        var path = $"/api/v1/portal/executions/greeter/{execution.Id}";

        var old = await SendAsync(HttpMethod.Get, path, key, "anna", HttpStatusCode.OK);
        Assert.Equal("success", (string?)old["status"]);
        Assert.True((bool)old["expired"]!);
        Assert.Null(old["result"]);
        Assert.Null(old["values"]);
    }

    private async Task<string[]> ListedAsync(string key, string user, string? roles)
        => ((JsonArray)await SendAsync(HttpMethod.Get, "/api/v1/portal/workflows", key, user, HttpStatusCode.OK, roles: roles))
            .Select(w => (string)w!["id"]!).Order().ToArray();

    [Fact]
    public async Task Roles_decide_which_workflows_the_user_sees()
    {
        var key = await KeyAsync(PipeApiRights.Portal);

        // no roles: only what has none (greeter, failing)
        Assert.Equal(["failing", "greeter"], await ListedAsync(key, "anna", null));
        // finance: the group's workflow, not the one that names hr
        Assert.Equal(["failing", "greeter", "payroll"], await ListedAsync(key, "anna", "finance"));
        // hr: only the workflow that names it; roles are sent percent-encoded and compared ignoring case
        Assert.Equal(["failing", "greeter", "hr-only"], await ListedAsync(key, "anna", "HR"));
        Assert.Equal(["failing", "greeter", "hr-only", "payroll"], await ListedAsync(key, "anna", "hr,finance"));
        Assert.Equal(["failing", "greeter"], await ListedAsync(key, "anna", "caf%C3%A9"));
    }

    [Fact]
    public async Task A_user_without_the_role_cannot_run_or_look_at_the_workflow()
    {
        var key = await KeyAsync(PipeApiRights.Portal);

        await SendAsync(HttpMethod.Post, "/api/v1/portal/workflows/payroll/run", key, "anna", HttpStatusCode.NotFound, new { values = new { } });
        await SendAsync(HttpMethod.Post, "/api/v1/portal/workflows/payroll/run", key, "anna", HttpStatusCode.NotFound, new { values = new { } }, roles: "hr");

        var done = await SendAsync(HttpMethod.Post, "/api/v1/portal/workflows/payroll/run", key, "anna", HttpStatusCode.OK, new { values = new { } }, roles: "finance");
        var path = $"/api/v1/portal/executions/payroll/{(string)done["executionId"]!}";
        await SendAsync(HttpMethod.Get, path, key, "anna", HttpStatusCode.OK, roles: "finance");

        // the role is gone: the run is not shown, neither in the list nor in detail, and can't be stopped
        await SendAsync(HttpMethod.Get, path, key, "anna", HttpStatusCode.NotFound);
        await SendAsync(HttpMethod.Post, path + "/cancel", key, "anna", HttpStatusCode.NotFound);
        var runs = (JsonArray)await SendAsync(HttpMethod.Get, "/api/v1/portal/executions", key, "anna", HttpStatusCode.OK);
        Assert.DoesNotContain(runs, r => (string?)r!["workflowId"] == "payroll");
        var withRole = (JsonArray)await SendAsync(HttpMethod.Get, "/api/v1/portal/executions", key, "anna", HttpStatusCode.OK, roles: "finance");
        Assert.Contains(withRole, r => (string?)r!["workflowId"] == "payroll");
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
