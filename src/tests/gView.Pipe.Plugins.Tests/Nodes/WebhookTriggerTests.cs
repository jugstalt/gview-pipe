using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.AspNetCore.Extensions.Routing;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.Credentials;
using gView.Pipe.Plugins.Nodes.Utility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Tests.Nodes;

/// <summary>Webhooks end to end: HTTP request → route → trigger → execution → HTTP response.</summary>
public sealed class WebhookTriggerTests : IAsyncLifetime
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

        _app = builder.Build();
        _app.MapPipeWebhooks(options => options.MaxBodyBytes = 4096);
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<IReadOnlyList<string>> ActivateAsync(PipeWorkflow workflow)
    {
        await _app.Services.GetRequiredService<IPipeWorkflowStore>().SaveAsync(workflow);
        return await _app.Services.GetRequiredService<IPipeTriggerManager>().ActivateAsync(workflow);
    }

    static private PipeWorkflow Workflow(string path, object? webhookParameters = null, string? id = null)
        => new PipeWorkflowBuilder("Orders", id)
            .Node("hook", PipeBuiltInNodes.WebhookTrigger, webhookParameters ?? new { path, responseMode = "whenFinished" })
            .Node("set", PipeBuiltInNodes.Set, new { fields = new { received = "{{ $json.body.name }}" } })
            .Connect("hook", "set")
            .Configure(w => w.Active = true)
            .Build();

    private async Task<PipeExecution> StoredExecutionAsync(string workflowId)
    {
        var store = _app.Services.GetRequiredService<IPipeExecutionStore>();
        for (var i = 0; i < 50; i++)
        {
            if ((await store.ListAsync(workflowId)).FirstOrDefault() is { } execution)
            {
                return execution;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("No execution was stored");
    }

    [Fact]
    public async Task Waiting_webhook_returns_the_output_of_the_last_node()
    {
        var workflow = Workflow("orders");
        Assert.Empty(await ActivateAsync(workflow));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhook/orders?source=test")
        {
            Content = JsonContent.Create(new { name = "Ada" })
        };
        request.Headers.Add("Cookie", "session=secret");
        request.Headers.Add("X-Custom", "visible");

        var response = await _client.SendAsync(request);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ada", body["received"]!.GetValue<string>());
        Assert.Equal("test", body["query"]!["source"]!.GetValue<string>());

        var headers = body["headers"]!.AsObject();
        Assert.Equal("visible", headers["x-custom"]!.GetValue<string>());
        Assert.False(headers.ContainsKey("cookie"));
    }

    static private PipeWorkflow RespondingWorkflow(string path, object respondParameters, bool withRespondNode = true)
    {
        var builder = new PipeWorkflowBuilder("Api")
            .Node("hook", PipeBuiltInNodes.WebhookTrigger, new { path, responseMode = "usingRespondNode", timeoutSeconds = 5 })
            .Node("respond", withRespondNode ? PipeBuiltInNodes.RespondToWebhook : PipeBuiltInNodes.NoOp, respondParameters)
            // the workflow goes on after responding
            .Node("after", PipeBuiltInNodes.Wait, new { amount = 0.2 })
            .Connect("hook", "respond")
            .Connect("respond", "after");
        return builder.Configure(w => w.Active = true).Build();
    }

    [Fact]
    public async Task Respond_node_sets_status_body_and_headers_before_the_run_ends()
    {
        await ActivateAsync(RespondingWorkflow("api/orders", new
        {
            respondWith = "customBody",
            body = "{{ { accepted: $json.body.name } }}",
            statusCode = 201,
            headers = new Dictionary<string, string> { ["X-Order"] = "{{ $json.body.name }}" }
        }));

        var response = await _client.PostAsJsonAsync("/webhook/api/orders", new { name = "Ada" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Ada", response.Headers.GetValues("X-Order").Single());
        Assert.Equal("""{"accepted":"Ada"}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Respond_node_can_send_text_with_its_own_content_type()
    {
        await ActivateAsync(RespondingWorkflow("page", new
        {
            respondWith = "customBody",
            body = "<h1>Hello {{ $json.body.name }}</h1>",
            headers = new Dictionary<string, string> { ["Content-Type"] = "text/html; charset=utf-8" }
        }));

        var response = await _client.PostAsJsonAsync("/webhook/page", new { name = "Ada" });

        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("<h1>Hello Ada</h1>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Run_without_respond_node_answers_500()
    {
        await ActivateAsync(RespondingWorkflow("silent", new { }, withRespondNode: false));

        var response = await _client.PostAsJsonAsync("/webhook/silent", new { name = "Ada" });
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("without reaching a Respond to Webhook node", body["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task Immediate_webhook_answers_202_and_runs_in_the_background()
    {
        var workflow = Workflow("later", new { path = "later", responseMode = "immediately" });
        await ActivateAsync(workflow);

        var response = await _client.PostAsJsonAsync("/webhook/later", new { name = "Grace" });
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var execution = await StoredExecutionAsync(workflow.Id);
        Assert.Equal(body["executionId"]!.GetValue<string>(), execution.Id);
        Assert.Equal(PipeExecutionMode.Trigger, execution.Mode);
    }

    [Fact]
    public async Task Credential_protects_the_webhook()
    {
        var credential = new PipeCredential
        {
            Name = "Hook token",
            TypeId = Guid.Parse(PipeBuiltInCredentials.BearerToken),
            Data = PipeJson.ToJsonObject(new BearerTokenCredential { Token = "s3cret" })
        };
        await _app.Services.GetRequiredService<IPipeCredentialStore>().SaveAsync(credential);
        await ActivateAsync(Workflow("secure", new { path = "secure", responseMode = "whenFinished", credentialId = credential.Id }));

        var anonymous = await _client.PostAsJsonAsync("/webhook/secure", new { name = "x" });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhook/secure") { Content = JsonContent.Create(new { name = "x" }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "s3cret");
        var authorized = await _client.SendAsync(request);
        var body = JsonNode.Parse(await authorized.Content.ReadAsStringAsync())!;

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        Assert.False(body["headers"]!.AsObject().ContainsKey("authorization"));
    }

    [Fact]
    public async Task Uploaded_files_become_binary_attachments()
    {
        var workflow = Workflow("upload");
        await ActivateAsync(workflow);

        using var form = new MultipartFormDataContent
        {
            { new StringContent("Ada"), "name" },
            { new ByteArrayContent("hello file"u8.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("text/plain") } }, "document", "notes.txt" }
        };
        var response = await _client.PostAsync("/webhook/upload", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = (await StoredExecutionAsync(workflow.Id)).NodeRuns["hook"].Outputs![PipePorts.Main][0];
        Assert.Equal("Ada", item.Json["body"]!["name"]!.GetValue<string>());
        var file = item.Binary["document"];
        Assert.Equal(("notes.txt", "text/plain", 10L), (file.FileName, file.MimeType, file.Length));
    }

    [Fact]
    public async Task Unknown_paths_wrong_methods_and_large_bodies_are_rejected()
    {
        await ActivateAsync(Workflow("strict"));

        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/webhook/nothing-here", null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _client.GetAsync("/webhook/strict")).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,
            (await _client.PostAsync("/webhook/strict", new StringContent(new string('x', 5000)))).StatusCode);
    }

    [Fact]
    public async Task Failing_workflows_answer_500_with_the_execution_id()
    {
        var workflow = new PipeWorkflowBuilder("Broken")
            .Node("hook", PipeBuiltInNodes.WebhookTrigger, new { path = "broken", responseMode = "whenFinished" })
            .Node("code", PipeBuiltInNodes.Code, new { code = "throw new Error('nope');" })
            .Connect("hook", "code")
            .Configure(w => w.Active = true)
            .Build();
        await ActivateAsync(workflow);

        var response = await _client.PostAsync("/webhook/broken", null);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("nope", body["error"]!.GetValue<string>());
        Assert.NotNull(body["executionId"]);
    }

    [Fact]
    public async Task A_path_used_by_another_workflow_is_reported_on_activation()
    {
        Assert.Empty(await ActivateAsync(Workflow("shared", id: "first")));

        var problems = await ActivateAsync(Workflow("shared", id: "second"));

        Assert.Contains("already used", Assert.Single(problems));
    }

    [Fact]
    public void Hints_show_the_url_and_warn_about_public_webhooks()
    {
        var registry = _app.Services.GetRequiredService<IPipeNodeRegistry>();
        var hints = registry.GetHints(registry.Find(Guid.Parse(PipeBuiltInNodes.WebhookTrigger))!, new JsonObject { ["path"] = "Orders/" });

        Assert.Contains(hints, h => h.Level == PipeNodeHintLevel.Info && h.Message.Contains("/webhook/orders"));
        Assert.Contains(hints, h => h.Level == PipeNodeHintLevel.Warning && h.Message.Contains("Public"));
    }
}
