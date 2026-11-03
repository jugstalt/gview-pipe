using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.Credentials;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Tests.Nodes;

public class OAuth2CredentialTests
{
    /// <summary>A token endpoint that numbers its tokens, and an API that records what it was sent.</summary>
    private sealed class FakeProvider : HttpMessageHandler
    {
        public int TokenRequests { get; private set; }
        public List<string?> ApiAuthorization { get; } = new();
        public List<string> TokenBodies { get; } = new();
        public string? TokenAuthorization { get; private set; }
        public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode ApiStatus { get; set; } = HttpStatusCode.OK;
        public string TokenResponse { get; set; } = """{ "access_token": "TOKEN{n}", "expires_in": 3600 }""";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "token.example.test")
            {
                TokenRequests++;
                TokenAuthorization = request.Headers.Authorization?.ToString();
                TokenBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                return new HttpResponseMessage(TokenStatus)
                {
                    Content = new StringContent(TokenResponse.Replace("{n}", TokenRequests.ToString()), Encoding.UTF8, "application/json")
                };
            }

            ApiAuthorization.Add(request.Headers.Authorization?.ToString());
            return new HttpResponseMessage(ApiStatus) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        }
    }

    static private async Task<(ServiceProvider Services, string CredentialId)> SetupAsync(FakeProvider provider, OAuth2ClientCredentialsCredential? data = null)
    {
        var services = PluginTestHost.Create(s => s
            .AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => provider));

        var credential = new PipeCredential
        {
            Name = "API",
            TypeId = Guid.Parse(PipeBuiltInCredentials.OAuth2ClientCredentials),
            Data = PipeJson.ToJsonObject(data ?? new OAuth2ClientCredentialsCredential
            {
                TokenUrl = "https://token.example.test/oauth/token",
                ClientId = "my client",
                ClientSecret = "s3cret/+",
                Scope = "read write"
            })
        };
        await services.GetRequiredService<IPipeCredentialStore>().SaveAsync(credential);
        return (services, credential.Id);
    }

    static private async Task<PipeExecution> CallAsync(ServiceProvider services, string credentialId, int items = 1)
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("http", PipeBuiltInNodes.HttpRequest, new { url = "https://api.example.test/x", credentialId })
            .Connect("start", "http")
            .Build();
        var request = new PipeExecutionRequest
        {
            InputItems = Enumerable.Range(0, items).Select(_ => new PipeItem(new JsonObject())).ToList()
        };
        return await services.GetRequiredService<IPipeEngine>().ExecuteAsync(workflow, request);
    }

    [Fact]
    public async Task Fetches_a_token_once_sends_it_as_bearer_and_reuses_it()
    {
        var provider = new FakeProvider();
        var (services, id) = await SetupAsync(provider);
        using var _ = services;

        var first = await CallAsync(services, id, items: 2);
        var second = await CallAsync(services, id);

        Assert.True(first.Status == PipeExecutionStatus.Success, first.Error);
        Assert.True(second.Status == PipeExecutionStatus.Success, second.Error);
        Assert.Equal(1, provider.TokenRequests);
        Assert.Equal(["Bearer TOKEN1", "Bearer TOKEN1", "Bearer TOKEN1"], provider.ApiAuthorization);

        // client id and secret are form-encoded before Basic, and the grant carries the scope
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Uri.EscapeDataString("my client")}:{Uri.EscapeDataString("s3cret/+")}"));
        Assert.Equal($"Basic {expected}", provider.TokenAuthorization);
        Assert.Equal("grant_type=client_credentials&scope=read+write", provider.TokenBodies.Single());
    }

    [Fact]
    public async Task Can_send_the_client_credentials_in_the_body_and_adds_the_audience()
    {
        var provider = new FakeProvider();
        var (services, id) = await SetupAsync(provider, new OAuth2ClientCredentialsCredential
        {
            TokenUrl = "https://token.example.test/t",
            ClientId = "id",
            ClientSecret = "secret",
            Audience = "https://api",
            ClientAuthentication = OAuth2ClientAuthentication.Body
        });
        using var _ = services;

        var execution = await CallAsync(services, id);

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        Assert.Null(provider.TokenAuthorization);
        Assert.Equal("grant_type=client_credentials&audience=https%3A%2F%2Fapi&client_id=id&client_secret=secret", provider.TokenBodies.Single());
    }

    [Fact]
    public async Task A_401_drops_the_token_so_the_next_run_fetches_a_new_one()
    {
        var provider = new FakeProvider { ApiStatus = HttpStatusCode.Unauthorized };
        var (services, id) = await SetupAsync(provider);
        using var _ = services;

        var failed = await CallAsync(services, id);
        Assert.Equal(PipeExecutionStatus.Error, failed.Status);

        provider.ApiStatus = HttpStatusCode.OK;
        var again = await CallAsync(services, id);

        Assert.True(again.Status == PipeExecutionStatus.Success, again.Error);
        Assert.Equal(2, provider.TokenRequests);
        Assert.Equal(["Bearer TOKEN1", "Bearer TOKEN2"], provider.ApiAuthorization);
    }

    [Fact]
    public async Task A_failing_token_request_fails_the_node_without_the_secret()
    {
        var provider = new FakeProvider
        {
            TokenStatus = HttpStatusCode.BadRequest,
            TokenResponse = """{ "error": "invalid_client", "error_description": "bad credentials" }"""
        };
        var (services, id) = await SetupAsync(provider);
        using var _ = services;

        var execution = await CallAsync(services, id);

        Assert.Equal(PipeExecutionStatus.Error, execution.Status);
        Assert.Contains("HTTP 400", execution.Error);
        Assert.Contains("invalid_client", execution.Error);
        Assert.DoesNotContain("s3cret", execution.Error);
        Assert.Empty(provider.ApiAuthorization);
    }

    [Fact]
    public async Task An_answer_without_a_token_is_an_error()
    {
        var provider = new FakeProvider { TokenResponse = """{ "token_type": "bearer" }""" };
        var (services, id) = await SetupAsync(provider);
        using var _ = services;

        var execution = await CallAsync(services, id);

        Assert.Equal(PipeExecutionStatus.Error, execution.Status);
        Assert.Contains("access_token", execution.Error);
    }
}
