using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Cli;

/// <summary>The answer of the API: the status, the JSON body (if any) and the ETag.</summary>
public sealed record PipeCliResponse(HttpStatusCode Status, JsonNode? Body, string? ETag)
{
    public bool Ok => (int)Status is >= 200 and < 300;

    /// <summary>"404 Workflow 'x' not found" plus the problems of a 422, one per line.</summary>
    public string Describe()
    {
        var text = new StringBuilder($"{(int)Status} {Body?["title"]?.GetValue<string>() ?? Status.ToString()}");
        if (Body?["details"]?["errors"] is JsonArray errors)
        {
            foreach (var error in errors)
            {
                text.Append("\n  - ").Append(error?.ToString());
            }
        }
        return text.ToString();
    }
}

/// <summary>Talks to <c>/api/v1</c> with the API key (ADR 0026).</summary>
public sealed class PipeCliClient
{
    public const string KeyHeader = "X-Pipe-Api-Key";

    private readonly HttpClient _http;

    public PipeCliClient(HttpClient http, string? key)
    {
        _http = http;
        // a client handed in may have been used with another key before
        _http.DefaultRequestHeaders.Remove(KeyHeader);
        if (!string.IsNullOrWhiteSpace(key))
        {
            _http.DefaultRequestHeaders.Add(KeyHeader, key);
        }
    }

    public Task<PipeCliResponse> GetAsync(string path) => SendAsync(HttpMethod.Get, path);

    public Task<PipeCliResponse> PostAsync(string path, JsonNode? body = null) => SendAsync(HttpMethod.Post, path, body);

    public Task<PipeCliResponse> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path);

    /// <param name="ifMatch">The ETag of the version the caller saw; the server answers 412 if it changed.</param>
    public Task<PipeCliResponse> PutAsync(string path, JsonNode body, string? ifMatch = null) => SendAsync(HttpMethod.Put, path, body, ifMatch);

    private async Task<PipeCliResponse> SendAsync(HttpMethod method, string path, JsonNode? body = null, string? ifMatch = null)
    {
        // relative to the base address, so a server below a path (https://host/pipe/) works too
        using var request = new HttpRequestMessage(method, "api/v1/" + path.TrimStart('/'));
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        using var response = await _http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        JsonNode? json = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try { json = JsonNode.Parse(text); }
            catch (JsonException) { }
        }
        return new PipeCliResponse(response.StatusCode, json, response.Headers.ETag?.ToString());
    }
}
