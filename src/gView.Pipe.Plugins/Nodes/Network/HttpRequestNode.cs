using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;
using gView.Pipe.Plugins.Credentials;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Network;

public enum HttpResponseFormat
{
    [PipeEnumValue("JSON when the server says so, text otherwise")]
    Auto,

    [PipeEnumValue("Read the answer as JSON", Label = "JSON")]
    Json,

    [PipeEnumValue("Keep the answer as text, e.g. HTML or CSV")]
    Text
}

public sealed class HttpRequestNodeParameters
{
    [PipeOptions, PipeParameter(Label = "Method", Order = 1, Expressions = false, Description = "What the request does")]
    public string Method { get; set; } = "GET";

    [Required, PipeParameter(Label = "URL", Placeholder = "https://", Order = 2,
        Description = "Address of the request, e.g. https://api.example.com/orders/{{ $json.id }}")]
    public string Url { get; set; } = "";

    [PipeCredential(PipeBuiltInCredentials.BasicAuth, PipeBuiltInCredentials.BearerToken, PipeBuiltInCredentials.ApiKeyHeader, PipeBuiltInCredentials.OAuth2ClientCredentials)]
    [PipeParameter(Label = "Credential", Expressions = false, Order = 3, Description = "Signs in at the server: user and password, a token, an API key header or OAuth2 client credentials")]
    public string? CredentialId { get; set; }

    [PipeParameter(Label = "Query parameters", Order = 4, Description = "Added to the URL as ?name=value, e.g. status → open; values are encoded")]
    public Dictionary<string, string> Query { get; set; } = new();

    [PipeParameter(Label = "Body", Multiline = true, Order = 5, ShowWhen = nameof(Method), ShowWhenValues = ["POST", "PUT", "PATCH", "DELETE"],
        Description = "What to send: JSON such as { \"name\": \"{{ $json.name }}\" }, or text")]
    public JsonNode? Body { get; set; }

    [PipeParameter(Label = "Content type", Order = 6, Advanced = true, ShowWhen = nameof(Method), ShowWhenValues = ["POST", "PUT", "PATCH", "DELETE"],
        Description = "Kind of the body, e.g. application/json or text/plain")]
    public string ContentType { get; set; } = "application/json";

    [PipeParameter(Label = "Headers", Order = 7, Advanced = true, Description = "Request headers, e.g. Accept → application/json")]
    public Dictionary<string, string> Headers { get; set; } = new();

    [PipeParameter(Label = "Response format", Expressions = false, Order = 8, Advanced = true, Description = "How the answer is read into body")]
    public HttpResponseFormat ResponseFormat { get; set; } = HttpResponseFormat.Auto;

    [PipeParameter(Label = "Fail on HTTP error status", Expressions = false, Order = 9, Advanced = true,
        Description = "Stop with an error when the server answers 400 or higher; off: the item goes on with the statusCode")]
    public bool FailOnHttpError { get; set; } = true;

    [PipeParameter(Label = "Pause between requests (ms)", Expressions = false, Order = 10, Advanced = true,
        Description = "Waits this long before each request but the first, e.g. 1000 for the rate limit of an API; 0 = no pause")]
    [Range(0, 600000)]
    public int DelayMs { get; set; }
}

[PipeNode(PipeBuiltInNodes.HttpRequest, "HTTP Request",
    Category = "Network",
    Description = "Sends one HTTP request per item. Output: statusCode, headers, body.")]
[PipeNodeExamples]
public sealed class HttpRequestNode : PipeItemNodeBase<HttpRequestNodeParameters>, IPipeParameterOptions
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PipeOAuth2TokenCache _tokens;

    public HttpRequestNode(IHttpClientFactory httpClientFactory, PipeOAuth2TokenCache tokens)
    {
        _httpClientFactory = httpClientFactory;
        _tokens = tokens;
    }

    protected override TimeSpan DelayBetweenItems(HttpRequestNodeParameters parameters)
        => TimeSpan.FromMilliseconds(Math.Clamp(parameters.DelayMs, 0, 600000));

    static private readonly IReadOnlyList<PipeParameterOption> Methods =
    [
        new("GET", null, "Read data"),
        new("POST", null, "Send new data, e.g. create an order"),
        new("PUT", null, "Replace data"),
        new("PATCH", null, "Change parts of data"),
        new("DELETE", null, "Delete data"),
        new("HEAD", null, "Only the headers, e.g. to check that a URL exists")
    ];

    public Task<IReadOnlyList<PipeParameterOption>> GetOptionsAsync(string parameter, JsonObject parameters, bool refresh, CancellationToken cancellationToken)
        => Task.FromResult(parameter == "method" ? Methods : []);

    protected override async Task<PipeItem?> ProcessItemAsync(PipeItem item, HttpRequestNodeParameters parameters, IPipeNodeContext context, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(new HttpMethod(parameters.Method.ToUpperInvariant()), BuildUri(parameters));

        if (parameters.Body is not null)
        {
            var body = PipeConditions.ToText(parameters.Body);
            request.Content = new StringContent(body, Encoding.UTF8, parameters.ContentType);
        }

        foreach (var (name, value) in parameters.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                request.Content?.Headers.TryAddWithoutValidation(name, value);
            }
        }

        OAuth2ClientCredentialsCredential? oauth = null;
        if (!string.IsNullOrEmpty(parameters.CredentialId))
        {
            oauth = await ApplyCredentialAsync(request, parameters.CredentialId, context, cancellationToken);
        }

        var client = _httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (oauth is not null && response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // the token may be revoked; the next attempt (retry, next run) fetches a new one (ADR 0035)
            _tokens.Invalidate(parameters.CredentialId!, oauth);
        }

        if (parameters.FailOnHttpError && !response.IsSuccessStatusCode)
        {
            throw new PipeException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(content, 500)}");
        }

        var headers = new JsonObject();
        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        return new PipeItem(new JsonObject
        {
            ["statusCode"] = (int)response.StatusCode,
            ["headers"] = headers,
            ["body"] = ParseBody(content, response.Content.Headers.ContentType, parameters.ResponseFormat)
        });
    }

    static private Uri BuildUri(HttpRequestNodeParameters parameters)
    {
        if (parameters.Query.Count == 0)
        {
            return new Uri(parameters.Url);
        }

        var query = string.Join("&", parameters.Query.Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value)}"));
        return new Uri(parameters.Url + (parameters.Url.Contains('?') ? "&" : "?") + query);
    }

    /// <returns>The credential when it is an OAuth2 one, so a 401 can drop its token.</returns>
    private async Task<OAuth2ClientCredentialsCredential?> ApplyCredentialAsync(HttpRequestMessage request, string credentialId, IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var credential = await context.GetCredentialAsync(credentialId, cancellationToken);

        switch (credential.TypeId.ToString())
        {
            case PipeBuiltInCredentials.BasicAuth:
                var basic = await context.GetCredentialAsync<BasicAuthCredential>(credentialId, cancellationToken);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{basic.UserName}:{basic.Password}")));
                break;

            case PipeBuiltInCredentials.BearerToken:
                var bearer = await context.GetCredentialAsync<BearerTokenCredential>(credentialId, cancellationToken);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer.Token);
                break;

            case PipeBuiltInCredentials.ApiKeyHeader:
                var apiKey = await context.GetCredentialAsync<ApiKeyHeaderCredential>(credentialId, cancellationToken);
                request.Headers.TryAddWithoutValidation(apiKey.HeaderName, apiKey.Value);
                break;

            case PipeBuiltInCredentials.OAuth2ClientCredentials:
                var oauth = await context.GetCredentialAsync<OAuth2ClientCredentialsCredential>(credentialId, cancellationToken);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _tokens.GetTokenAsync(credentialId, oauth, cancellationToken));
                return oauth;

            default:
                throw new PipeException($"Credential '{credential.Name}' has a type the HTTP Request node can't use");
        }
        return null;
    }

    static private JsonNode? ParseBody(string content, MediaTypeHeaderValue? contentType, HttpResponseFormat format)
    {
        var asJson = format == HttpResponseFormat.Json
                     || (format == HttpResponseFormat.Auto && contentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true);

        if (asJson && content.Length > 0)
        {
            try
            {
                return JsonNode.Parse(content);
            }
            catch (JsonException) when (format == HttpResponseFormat.Auto)
            {
            }
        }

        return JsonValue.Create(content);
    }

    static private string Truncate(string text, int length) => text.Length <= length ? text : text[..length] + "…";
}
