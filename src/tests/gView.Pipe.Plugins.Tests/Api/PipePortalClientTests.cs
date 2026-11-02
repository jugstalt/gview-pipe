using gView.Pipe.Abstraction.Portal;
using gView.Pipe.Abstraction.Security;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.AspNetCore.Authentication;
using gView.Pipe.AspNetCore.Extensions.DependencyInjection;
using gView.Pipe.AspNetCore.Extensions.Routing;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.Nodes.Utility;
using gView.Pipe.Portal.Portal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Claims;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Tests.Api;

/// <summary>The portal's client against a real backend (ADR 0028): the key, the user header, forms, results.</summary>
public sealed class PipePortalClientTests : IAsyncLifetime
{
    private WebApplication _app = default!;
    private string _key = default!;

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

        _key = PipeApiKeys.Generate();
        await _app.Services.GetRequiredService<IPipeApiKeyStore>().SaveAsync(new PipeApiKey
        {
            Name = "portal",
            Hash = PipeApiKeys.Hash(_key),
            Rights = PipeApiRights.Portal
        });
        await _app.Services.GetRequiredService<IPipeWorkflowGroupStore>().SaveAsync(new PipeWorkflowGroup { Id = "office", Name = "Office" });
        await _app.Services.GetRequiredService<IPipeWorkflowStore>().SaveAsync(new PipeWorkflowBuilder("Greeter", "greeter")
            .Node("form", PipeBuiltInNodes.FormTrigger, new
            {
                fields = new object[]
                {
                    new { name = "name", type = "text", required = true },
                    new { name = "plate", type = "text", pattern = @"[A-Z]{1,2}-\d{1,5}", patternMessage = "Like WL-123" }
                }
            })
            .Node("result", PipeBuiltInNodes.Result, new { kind = "text", title = "Greeting", content = "Hello {{ $json.name }}" })
            .Connect("form", "result")
            .Configure(w =>
            {
                w.GroupId = "office";
                w.Settings.Portal = new PipeWorkflowPortalSettings { Published = true };
            })
            .Build());
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private PipePortalClient ClientFor(string user, string? email = null, params string[] roles)
    {
        var http = _app.GetTestClient();
        http.DefaultRequestHeaders.Add(PipePortalClient.KeyHeader, _key);
        return new PipePortalClient(http, new FixedUser(user, email, roles), new PipeAuthenticationOptions());
    }

    [Fact]
    public async Task The_client_lists_published_workflows_with_category_and_form()
    {
        var workflows = await ClientFor("anna").GetWorkflowsAsync();

        var greeter = Assert.Single(workflows);
        Assert.Equal("Office", greeter.Category);
        Assert.Equal(["name", "plate"], greeter.Form.Select(f => f.Name));
        Assert.True(greeter.Form[0].Required);
        Assert.Equal("Like WL-123", greeter.Form[1].PatternMessage);
    }

    [Fact]
    public async Task Problems_of_the_form_come_back_per_field()
    {
        var ex = await Assert.ThrowsAsync<PipePortalException>(
            () => ClientFor("anna").RunAsync("greeter", new JsonObject { ["plate"] = "nope" }, PipePortalRunMode.Wait));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.Status);
        Assert.Equal("Required", ex.Fields["name"]);
        Assert.Equal("Like WL-123", ex.Fields["plate"]);
    }

    [Fact]
    public async Task A_run_shows_its_result_to_its_user_only()
    {
        var anna = ClientFor("anna");

        var run = await anna.RunAsync("greeter", new JsonObject { ["name"] = "Anna", ["plate"] = "WL-1" }, PipePortalRunMode.Wait);

        Assert.Equal("success", run.Status);
        Assert.Equal("Hello Anna", run.Result?.Text);
        Assert.Equal("Greeting", run.Result?.Title);
        Assert.Equal(run.ExecutionId, Assert.Single(await anna.GetRunsAsync()).ExecutionId);
        Assert.Equal("Hello Anna", (await anna.GetRunAsync("greeter", run.ExecutionId)).Result?.Text);

        var ben = ClientFor("ben");
        Assert.Empty(await ben.GetRunsAsync());
        var denied = await Assert.ThrowsAsync<PipePortalException>(() => ben.GetRunAsync("greeter", run.ExecutionId));
        Assert.Equal(HttpStatusCode.NotFound, denied.Status);
    }

    [Fact]
    public async Task The_client_knows_the_address_of_the_user_and_whether_the_backend_can_mail()
    {
        Assert.Equal("anna@example.com", await ClientFor("anna", "anna@example.com").GetEmailAsync());
        Assert.Null(await ClientFor("anna").GetEmailAsync());
        // the backend of this test has no notification workflow
        Assert.False((await ClientFor("anna").GetInfoAsync()).MailWhenDone);
        Assert.Equal(30, (await ClientFor("anna").GetInfoAsync()).HistoryDays);
    }

    [Fact]
    public async Task A_run_comes_back_with_the_values_to_run_it_again()
    {
        var anna = ClientFor("anna");
        var run = await anna.RunAsync("greeter", new JsonObject { ["name"] = "Anna", ["plate"] = "WL-7" }, PipePortalRunMode.Wait);

        var again = await anna.GetRunAsync("greeter", run.ExecutionId);

        Assert.Equal("Anna", (string?)again.Values?["name"]);
        Assert.Equal("WL-7", (string?)again.Values?["plate"]);
        Assert.False(again.Expired);
    }

    [Fact]
    public async Task Without_a_login_the_user_is_anonymous()
    {
        var run = await ClientFor("").RunAsync("greeter", new JsonObject { ["name"] = "Nobody" }, PipePortalRunMode.Wait);

        var stored = await _app.Services.GetRequiredService<IPipeExecutionStore>().GetAsync("greeter", run.ExecutionId);
        Assert.Equal("portal:" + PipePortalClient.AnonymousUser, stored!.StartedBy);
    }

    /// <summary>A signed-in user, or nobody for an empty name.</summary>
    private sealed class FixedUser(string name, string? email = null, string[]? roles = null) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.Name, name) };
            if (email is not null)
            {
                claims.Add(new Claim("email", email));
            }
            claims.AddRange((roles ?? []).Select(r => new Claim("role", r)));
            return Task.FromResult(new AuthenticationState(name.Length == 0
                ? new ClaimsPrincipal(new ClaimsIdentity())
                : new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));
        }
    }
}
