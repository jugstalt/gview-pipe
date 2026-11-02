using gView.Pipe.Abstraction.Portal;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Workflows;
using Microsoft.AspNetCore.Components.Authorization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Portal.Portal;

/// <summary>Settings of the portal (section <c>Portal</c>).</summary>
public sealed class PipePortalOptions
{
    /// <summary>Address of the backend, e.g. <c>https://pipe.internal:7240</c>.</summary>
    public string BackendUrl { get; set; } = "";

    /// <summary>Key with the right portal. Stays on this server; never sent to the browser.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Shown in the title bar.</summary>
    public string Title { get; set; } = "gView.Pipe";

    /// <summary>Seconds between two status requests while a run goes on.</summary>
    public int PollSeconds { get; set; } = 2;
}

/// <summary>A published workflow, as the portal lists it.</summary>
public sealed class PortalWorkflow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? CategoryId { get; set; }
    public List<PipeFormField> Form { get; set; } = [];
    public PipePortalRunMode DefaultMode { get; set; }
    public bool AllowModeChoice { get; set; }
    public int WaitTimeoutSeconds { get; set; }
    public bool AllowCancel { get; set; }
}

/// <summary>A run of the user, in the list or in detail.</summary>
public sealed class PortalRun
{
    public string ExecutionId { get; set; } = "";
    public string WorkflowId { get; set; } = "";
    public string? WorkflowName { get; set; }

    /// <summary>queued, running, success, error or canceled.</summary>
    public string Status { get; set; } = "";
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int? QueuePosition { get; set; }
    public bool CanCancel { get; set; }
    public PipeResultView? Result { get; set; }

    /// <summary>What the user entered (without passwords); null when it isn't kept (anymore).</summary>
    public JsonObject? Values { get; set; }

    /// <summary>The result and the values are not kept any longer; only the status is shown.</summary>
    public bool Expired { get; set; }

    public string? Error { get; set; }

    public bool IsRunning => Status is "queued" or "running";
}

/// <summary>What the backend offers the portal.</summary>
public sealed class PortalInfo
{
    /// <summary>A mail can be sent when a run is done.</summary>
    public bool MailWhenDone { get; set; }

    /// <summary>Days results and values are kept.</summary>
    public int HistoryDays { get; set; }
}

/// <summary>The backend refused a request; <see cref="Fields"/> holds the problems of a form (422).</summary>
public sealed class PipePortalException : Exception
{
    public PipePortalException(HttpStatusCode status, string message, IReadOnlyDictionary<string, string>? fields = null) : base(message)
    {
        Status = status;
        Fields = fields ?? new Dictionary<string, string>();
    }

    public HttpStatusCode Status { get; }

    public IReadOnlyDictionary<string, string> Fields { get; }
}

/// <summary>
/// Calls the portal endpoints of the backend (ADR 0027) for the signed-in user: the key authenticates the portal, the
/// header names the user.
/// </summary>
public sealed class PipePortalClient
{
    public const string KeyHeader = "X-Pipe-Api-Key";
    public const string UserHeader = "X-Pipe-On-Behalf-Of";
    public const string AnonymousUser = "anonymous";

    private readonly HttpClient _http;
    private readonly AuthenticationStateProvider _authentication;

    public PipePortalClient(HttpClient http, AuthenticationStateProvider authentication)
    {
        _http = http;
        _authentication = authentication;
    }

    public async Task<PortalInfo> GetInfoAsync(CancellationToken cancellationToken = default)
        => await SendAsync<PortalInfo>(HttpMethod.Get, "api/v1/portal/info", null, cancellationToken) ?? new PortalInfo();

    /// <summary>The address of the user from the login (claim <c>email</c>), or null.</summary>
    public async Task<string?> GetEmailAsync()
    {
        var user = (await _authentication.GetAuthenticationStateAsync()).User;
        var email = user.FindFirst("email")?.Value ?? user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        return string.IsNullOrWhiteSpace(email) ? null : email;
    }

    public async Task<IReadOnlyList<PortalWorkflow>> GetWorkflowsAsync(CancellationToken cancellationToken = default)
        => await SendAsync<List<PortalWorkflow>>(HttpMethod.Get, "api/v1/portal/workflows", null, cancellationToken) ?? [];

    /// <summary>Starts a run. A finished run (waiting) comes back with its result, otherwise with the status only.</summary>
    public async Task<PortalRun> RunAsync(string workflowId, JsonObject values, PipePortalRunMode? mode, string? notifyEmail = null, CancellationToken cancellationToken = default)
        => (await SendAsync<PortalRun>(HttpMethod.Post, $"api/v1/portal/workflows/{Uri.EscapeDataString(workflowId)}/run",
            new { values, mode, notifyEmail }, cancellationToken))!;

    public async Task<IReadOnlyList<PortalRun>> GetRunsAsync(CancellationToken cancellationToken = default)
        => await SendAsync<List<PortalRun>>(HttpMethod.Get, "api/v1/portal/executions", null, cancellationToken) ?? [];

    public async Task<PortalRun> GetRunAsync(string workflowId, string executionId, CancellationToken cancellationToken = default)
        => (await SendAsync<PortalRun>(HttpMethod.Get, $"api/v1/portal/executions/{Uri.EscapeDataString(workflowId)}/{Uri.EscapeDataString(executionId)}", null, cancellationToken))!;

    public Task CancelAsync(string workflowId, string executionId, CancellationToken cancellationToken = default)
        => SendAsync<JsonObject>(HttpMethod.Post, $"api/v1/portal/executions/{Uri.EscapeDataString(workflowId)}/{Uri.EscapeDataString(executionId)}/cancel", null, cancellationToken);

    /// <summary>The name the backend gets: what the login says, or <see cref="AnonymousUser"/> when there is no login.</summary>
    private async Task<string> UserAsync()
    {
        var user = (await _authentication.GetAuthenticationStateAsync()).User;
        return user.Identity is { IsAuthenticated: true, Name: { Length: > 0 } name } ? name : AnonymousUser;
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add(UserHeader, await UserAsync());
        if (body is not null)
        {
            request.Content = new StringContent(PipeJson.Serialize(body), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new PipePortalException(HttpStatusCode.ServiceUnavailable, $"The server can't be reached: {ex.Message}");
        }
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw Problem(response.StatusCode, text);
            }
            return text.Length == 0 ? default : PipeJson.Deserialize<T>(text);
        }
    }

    /// <summary>A problem+json of the backend as an exception; the fields of a 422 are in <c>details</c>.</summary>
    static private PipePortalException Problem(HttpStatusCode status, string text)
    {
        string? title = null;
        Dictionary<string, string>? fields = null;
        try
        {
            if (JsonNode.Parse(text) is JsonObject problem)
            {
                title = (string?)problem["title"];
                if (problem["details"] is JsonObject details)
                {
                    fields = details.ToDictionary(p => p.Key, p => p.Value?.ToString() ?? "");
                }
            }
        }
        catch (JsonException)
        {
            // not a problem document: the status says enough
        }
        return new PipePortalException(status, title ?? status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "The portal isn't allowed to use the server; tell your administrator.",
            HttpStatusCode.NotFound => "Not found.",
            _ => $"The server answered {(int)status}."
        }, fields);
    }
}
