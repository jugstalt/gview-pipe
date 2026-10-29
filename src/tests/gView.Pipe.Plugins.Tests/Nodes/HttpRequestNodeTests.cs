using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.Credentials;
using gView.Pipe.Abstraction.Serialization;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;

namespace gView.Pipe.Plugins.Tests.Nodes;

public class HttpRequestNodeTests
{
    [Fact]
    public async Task Sends_request_with_credential_and_parses_json_response()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{ "id": 7, "ok": true }""", "application/json");
        using var services = PluginTestHost.Create(s => s
            .AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => handler));

        var credential = new PipeCredential
        {
            Name = "Api token",
            TypeId = Guid.Parse(PipeBuiltInCredentials.BearerToken),
            Data = PipeJson.ToJsonObject(new BearerTokenCredential { Token = "t0ken" })
        };
        await services.GetRequiredService<IPipeCredentialStore>().SaveAsync(credential);

        var workflow = new PipeWorkflowBuilder()
            .Node("http", PipeBuiltInNodes.HttpRequest, new
            {
                method = "post",
                url = "https://example.test/items/{{ $workflow.name }}",
                query = new Dictionary<string, string> { ["q"] = "a b" },
                headers = new Dictionary<string, string> { ["X-Test"] = "1" },
                body = new { name = "gView" },
                credentialId = credential.Id
            })
            .Configure(w => w.Name = "wf")
            .Build();

        var execution = await services.GetRequiredService<IPipeEngine>().ExecuteAsync(workflow);

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        var output = Assert.Single(execution.Output("http"));
        Assert.Equal(200, output["statusCode"]!.GetValue<int>());
        Assert.Equal(7, output["body"]!["id"]!.GetValue<int>());

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://example.test/items/wf?q=a%20b", request.Uri);
        Assert.Equal("Bearer t0ken", request.Authorization);
        Assert.Equal("1", request.TestHeader);
        Assert.Equal("""{"name":"gView"}""", request.Body);
    }

    [Fact]
    public async Task Error_status_fails_the_node()
    {
        var handler = new FakeHandler(HttpStatusCode.NotFound, "nope", "text/plain");
        using var services = PluginTestHost.Create(s => s
            .AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => handler));

        var workflow = new PipeWorkflowBuilder()
            .Node("http", PipeBuiltInNodes.HttpRequest, new { url = "https://example.test/missing" })
            .Build();

        var execution = await services.GetRequiredService<IPipeEngine>().ExecuteAsync(workflow);

        Assert.Equal(PipeExecutionStatus.Error, execution.Status);
        Assert.Contains("404", execution.Error);
    }

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string? Authorization, string? TestHeader, string? Body);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _content;
        private readonly string _mediaType;

        public FakeHandler(HttpStatusCode status, string content, string mediaType)
        {
            _status = status;
            _content = content;
            _mediaType = mediaType;
        }

        public List<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Test", out var values) ? values.Single() : null,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_content, Encoding.UTF8, _mediaType)
            };
        }
    }
}
