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
    /// <summary>JSON if the response says so, text otherwise.</summary>
    Auto,
    Json,
    Text
}

public sealed class HttpRequestNodeParameters
{
    [PipeParameter(Label = "Method", Order = 1)]
    public string Method { get; set; } = "GET";

    [Required, PipeParameter(Label = "URL", Placeholder = "https://", Order = 2)]
    public string Url { get; set; } = "";

    [PipeParameter(Label = "Query parameters", Order = 3, Description = "Added to the URL as ?name=value; values are URL-encoded")]
    public Dictionary<string, string> Query { get; set; } = new();

    [PipeParameter(Label = "Headers", Order = 4, Description = "Request headers, e.g. Accept or Content-Type")]
    public Dictionary<string, string> Headers { get; set; } = new();

    [PipeParameter(Label = "Body", Description = "JSON, or text if the value is a string", Multiline = true, Order = 5)]
    public JsonNode? Body { get; set; }

    [PipeParameter(Label = "Content type", Order = 6)]
    public string ContentType { get; set; } = "application/json";

    [PipeParameter(Label = "Response format", Expressions = false, Order = 7)]
    public HttpResponseFormat ResponseFormat { get; set; } = HttpResponseFormat.Auto;

    [PipeParameter(Label = "Fail on HTTP error status", Expressions = false, Order = 8)]
    public bool FailOnHttpError { get; set; } = true;

    [PipeCredential(PipeBuiltInCredentials.BasicAuth, PipeBuiltInCredentials.BearerToken, PipeBuiltInCredentials.ApiKeyHeader)]
    [PipeParameter(Label = "Credential", Expressions = false, Order = 9, Description = "Authenticates the request: Basic, Bearer token or an API key header")]
    public string? CredentialId { get; set; }
}

[PipeNode(PipeBuiltInNodes.HttpRequest, "HTTP Request",
    Category = "Network",
    Description = "Sends one HTTP request per item. Output: statusCode, headers, body.")]
public sealed class HttpRequestNode : PipeItemNodeBase<HttpRequestNodeParameters>
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HttpRequestNode(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

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

        if (!string.IsNullOrEmpty(parameters.CredentialId))
        {
            await ApplyCredentialAsync(request, parameters.CredentialId, context, cancellationToken);
        }

        var client = _httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

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

    static private async Task ApplyCredentialAsync(HttpRequestMessage request, string credentialId, IPipeNodeContext context, CancellationToken cancellationToken)
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

            default:
                throw new PipeException($"Credential '{credential.Name}' has a type the HTTP Request node can't use");
        }
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
