using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Plugins.Credentials;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Triggers;

public enum WebhookMethod
{
    Any,
    Get,
    Post,
    Put,
    Patch,
    Delete
}

public enum WebhookResponseMode
{
    /// <summary>202 with the execution id; the workflow runs in the background.</summary>
    Immediately,

    /// <summary>Waits for the execution and returns the items of the last executed node.</summary>
    WhenFinished
}

public sealed class WebhookTriggerNodeParameters
{
    [Required, PipeParameter(Label = "Path", Order = 1, Expressions = false, Placeholder = "orders/new",
        Description = "Called at /webhook/<path>; must be unique across active workflows")]
    public string Path { get; set; } = Guid.NewGuid().ToString("N")[..12];

    [PipeParameter(Label = "HTTP method", Order = 2, Expressions = false)]
    public WebhookMethod Method { get; set; } = WebhookMethod.Post;

    [PipeParameter(Label = "Respond", Order = 3, Expressions = false)]
    public WebhookResponseMode ResponseMode { get; set; } = WebhookResponseMode.Immediately;

    [PipeParameter(Label = "Wait timeout (seconds)", Order = 4, Expressions = false,
        Description = "When responding after the run: after this time 202 is returned and the run continues")]
    public int TimeoutSeconds { get; set; } = 30;

    [PipeCredential(PipeBuiltInCredentials.BasicAuth, PipeBuiltInCredentials.BearerToken, PipeBuiltInCredentials.ApiKeyHeader)]
    [PipeParameter(Label = "Credential", Order = 5, Expressions = false, Description = "Callers must authenticate with it; without one the webhook is public")]
    public string? CredentialId { get; set; }
}

[PipeNode(PipeBuiltInNodes.WebhookTrigger, "Webhook Trigger",
    Category = "Triggers",
    Description = "Starts the workflow when its URL is called while the workflow is active.")]
public sealed class WebhookTriggerNode : IPipeTriggerNode, IPipeNode<WebhookTriggerNodeParameters>, IPipeNodeHints
{
    // never copied into the item (and so never into the execution history)
    static private readonly HashSet<string> HiddenHeaders = new(StringComparer.OrdinalIgnoreCase) { "authorization", "proxy-authorization", "cookie" };

    public Task<IAsyncDisposable?> ActivateAsync(IPipeTriggerContext context, CancellationToken cancellationToken)
    {
        var parameters = context.GetParameters<WebhookTriggerNodeParameters>();
        var registration = context.Host.RegisterWebhook(new PipeWebhookRegistration(
            parameters.Path,
            parameters.Method == WebhookMethod.Any ? "*" : parameters.Method.ToString().ToUpperInvariant(),
            context.WorkflowId,
            context.WorkflowName,
            context.NodeName,
            (request, ct) => HandleAsync(context, parameters, request, ct)));

        return Task.FromResult<IAsyncDisposable?>(new Registration(registration));
    }

    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
        => Task.FromResult(PipeNodeResult.FromMain(context.GetInput()));

    public IReadOnlyList<PipeNodeHint> GetHints(JsonObject parameters)
    {
        WebhookTriggerNodeParameters? values;
        try
        {
            values = parameters.Deserialize<WebhookTriggerNodeParameters>(PipeJson.Options);
        }
        catch (JsonException)
        {
            values = null;
        }
        if (values is null)
        {
            return [];
        }

        var hints = new List<PipeNodeHint>();
        try
        {
            var method = values.Method == WebhookMethod.Any ? "any method" : values.Method.ToString().ToUpperInvariant();
            hints.Add(new(PipeNodeHintLevel.Info, $"Called with {method} at /webhook/{PipeWebhookPaths.Normalize(values.Path)} while the workflow is active."));
        }
        catch (PipeException ex)
        {
            hints.Add(new(PipeNodeHintLevel.Warning, ex.Message));
        }

        if (string.IsNullOrEmpty(values.CredentialId))
        {
            hints.Add(new(PipeNodeHintLevel.Warning, "Public webhook: anyone who knows the URL can start this workflow. Select a credential to require authentication."));
        }

        return hints;
    }

    static private async Task<PipeWebhookResponse> HandleAsync(
        IPipeTriggerContext context,
        WebhookTriggerNodeParameters parameters,
        PipeWebhookRequest request,
        CancellationToken cancellationToken)
    {
        var hiddenHeaders = new HashSet<string>(HiddenHeaders, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(parameters.CredentialId))
        {
            var (authorized, keyHeader) = await AuthorizeAsync(context, parameters.CredentialId, request, cancellationToken);
            if (!authorized)
            {
                var unauthorized = PipeWebhookResponse.Error(401, "Unauthorized");
                unauthorized.Headers["WWW-Authenticate"] = "Basic realm=\"gView.Pipe\"";
                return unauthorized;
            }
            if (keyHeader is not null)
            {
                hiddenHeaders.Add(keyHeader);
            }
        }

        var (item, binaryContent) = ToItem(request, hiddenHeaders);
        var executionId = Guid.NewGuid().ToString("N");

        // the run must not stop when the caller disconnects
        var run = context.FireAsync([item], CancellationToken.None, new PipeTriggerFireOptions { ExecutionId = executionId, BinaryContent = binaryContent });

        if (parameters.ResponseMode == WebhookResponseMode.Immediately)
        {
            _ = run.ContinueWith(
                task => context.Logger.LogError(task.Exception, "Webhook run {ExecutionId} failed", executionId),
                TaskContinuationOptions.OnlyOnFaulted);
            return PipeWebhookResponse.Json(202, new JsonObject { ["executionId"] = executionId, ["status"] = "started" });
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(1, parameters.TimeoutSeconds));
        if (await Task.WhenAny(run, Task.Delay(timeout, cancellationToken)) != run)
        {
            return PipeWebhookResponse.Json(202, new JsonObject { ["executionId"] = executionId, ["status"] = "running" });
        }

        return await run switch
        {
            null => PipeWebhookResponse.Error(429, "The workflow is already running (max concurrent runs reached)"),
            { Status: not PipeExecutionStatus.Success } failed => PipeWebhookResponse.Error(500, failed.Error ?? failed.Status.ToString(), executionId),
            var execution => PipeWebhookResponse.Json(200, ResponseBody(execution))
        };
    }

    static private (PipeItem Item, Dictionary<string, byte[]> BinaryContent) ToItem(PipeWebhookRequest request, HashSet<string> hiddenHeaders)
    {
        var headers = new JsonObject();
        foreach (var (name, value) in request.Headers.Where(h => !hiddenHeaders.Contains(h.Key)))
        {
            headers[name] = value;
        }

        var query = new JsonObject();
        foreach (var (name, value) in request.Query)
        {
            query[name] = value;
        }

        var item = new PipeItem(new JsonObject
        {
            ["method"] = request.Method,
            ["path"] = request.Path,
            ["query"] = query,
            ["headers"] = headers,
            ["body"] = request.Body?.DeepClone(),
            ["receivedAt"] = DateTimeOffset.UtcNow.ToString("O")
        });

        // file bytes travel next to the item and land in the execution's binary store
        var binaryContent = new Dictionary<string, byte[]>();
        foreach (var file in request.Files)
        {
            var name = file.FieldName;
            for (var i = 2; item.Binary.ContainsKey(name); i++)
            {
                name = $"{file.FieldName}_{i}";
            }

            var id = Guid.NewGuid().ToString("N");
            item.Binary[name] = new PipeBinaryRef(id, file.FileName, file.ContentType, file.Content.Length);
            binaryContent[id] = file.Content;
        }

        return (item, binaryContent);
    }

    /// <returns>Whether the request is authorized, and the header carrying an API key (to hide it).</returns>
    static private async Task<(bool Authorized, string? KeyHeader)> AuthorizeAsync(
        IPipeTriggerContext context,
        string credentialId,
        PipeWebhookRequest request,
        CancellationToken cancellationToken)
    {
        var credential = await context.GetCredentialAsync(credentialId, cancellationToken);
        var authorization = request.Headers.GetValueOrDefault("authorization") ?? "";

        switch (credential.TypeId.ToString())
        {
            case PipeBuiltInCredentials.BasicAuth:
                var basic = credential.Data.Deserialize<BasicAuthCredential>(PipeJson.Options)!;
                var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{basic.UserName}:{basic.Password}"));
                return (SecureEquals(authorization, $"Basic {expected}"), null);

            case PipeBuiltInCredentials.BearerToken:
                var bearer = credential.Data.Deserialize<BearerTokenCredential>(PipeJson.Options)!;
                return (SecureEquals(authorization, $"Bearer {bearer.Token}"), null);

            case PipeBuiltInCredentials.ApiKeyHeader:
                var apiKey = credential.Data.Deserialize<ApiKeyHeaderCredential>(PipeJson.Options)!;
                var header = apiKey.HeaderName.ToLowerInvariant();
                return (SecureEquals(request.Headers.GetValueOrDefault(header) ?? "", apiKey.Value), header);

            default:
                throw new PipeException($"Credential '{credential.Name}' has a type the webhook can't check");
        }
    }

    static private bool SecureEquals(string actual, string expected)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));

    /// <summary>Items of the node that finished last: one item as object, several as array.</summary>
    static private JsonNode ResponseBody(PipeExecution execution)
    {
        var last = execution.NodeRuns.Values
            .Where(r => r.Status == PipeNodeRunStatus.Success && r.Outputs is not null)
            .OrderByDescending(r => r.FinishedAt)
            .FirstOrDefault();

        var items = last?.Outputs is { } outputs
            ? outputs.TryGetValue(PipePorts.Main, out var main) ? main : outputs.Values.SelectMany(i => i).ToList()
            : [];

        return items.Count == 1
            ? items[0].Json.DeepClone()
            : new JsonArray(items.Select(i => (JsonNode)i.Json.DeepClone()).ToArray());
    }

    private sealed class Registration : IAsyncDisposable
    {
        private readonly IDisposable _registration;

        public Registration(IDisposable registration)
        {
            _registration = registration;
        }

        public ValueTask DisposeAsync()
        {
            _registration.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
