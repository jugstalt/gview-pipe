using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Triggers;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Tests.Triggers;

public class PipeWebhookRegistryTests
{
    static private PipeWebhookRegistration Webhook(string path, string method = "POST", string workflow = "wf", string body = "ok")
        => new(path, method, workflow, workflow, "Webhook", (_, _) => Task.FromResult(PipeWebhookResponse.Json(200, JsonValue.Create(body))));

    static private PipeWebhookRequest Request(string path, string method = "POST") => new() { Path = path, Method = method };

    [Theory]
    [InlineData("/Orders/New/", "orders/new")]
    [InlineData("hook_1", "hook_1")]
    public void Paths_are_normalized(string path, string expected)
    {
        Assert.Equal(expected, PipeWebhookPaths.Normalize(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../etc")]
    [InlineData("a b")]
    [InlineData("ä")]
    public void Invalid_paths_are_rejected(string path)
    {
        Assert.Throws<PipeException>(() => PipeWebhookPaths.Normalize(path));
    }

    [Fact]
    public async Task Routes_by_path_and_method()
    {
        using var services = PipeTestHost.Create();
        var host = services.GetRequiredService<IPipeTriggerHost>();
        var router = services.GetRequiredService<IPipeWebhookRouter>();

        using var _ = host.RegisterWebhook(Webhook("/Orders", "POST"));

        Assert.Equal(200, (await router.HandleAsync(Request("orders"), default)).StatusCode);
        var wrongMethod = await router.HandleAsync(Request("orders", "GET"), default);
        Assert.Equal(405, wrongMethod.StatusCode);
        Assert.Equal("POST", wrongMethod.Headers["Allow"]);
        Assert.Equal(404, (await router.HandleAsync(Request("other"), default)).StatusCode);
    }

    [Fact]
    public async Task A_path_belongs_to_one_webhook_until_it_is_released()
    {
        using var services = PipeTestHost.Create();
        var host = services.GetRequiredService<IPipeTriggerHost>();
        var router = services.GetRequiredService<IPipeWebhookRouter>();

        var first = host.RegisterWebhook(Webhook("hook", workflow: "First"));
        var conflict = Assert.Throws<PipeException>(() => host.RegisterWebhook(Webhook("HOOK", workflow: "Second")));
        Assert.Contains("First", conflict.Message);

        first.Dispose();
        using var second = host.RegisterWebhook(Webhook("hook", workflow: "Second", body: "second"));

        // disposing the old registration again must not remove the new one
        first.Dispose();
        Assert.Equal("second", (await router.HandleAsync(Request("hook"), default)).Body!.GetValue<string>());
    }

    [Fact]
    public async Task Any_method_and_handler_errors()
    {
        using var services = PipeTestHost.Create();
        var host = services.GetRequiredService<IPipeTriggerHost>();
        var router = services.GetRequiredService<IPipeWebhookRouter>();

        using var _ = host.RegisterWebhook(new PipeWebhookRegistration("broken", "*", "wf", "wf", "Webhook",
            (_, _) => throw new InvalidOperationException("kaputt")));

        var response = await router.HandleAsync(Request("broken", "DELETE"), default);

        Assert.Equal(500, response.StatusCode);
        Assert.Contains("kaputt", response.Body!["error"]!.GetValue<string>());
    }
}
