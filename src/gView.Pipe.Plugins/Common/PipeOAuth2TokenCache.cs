using gView.Pipe.Abstraction;
using gView.Pipe.Plugins.Credentials;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Common;

/// <summary>
/// Access tokens of OAuth2 client credentials, in memory and for the whole process (ADR 0035).
/// A token is renewed shortly before it expires; concurrent requests share one fetch.
/// </summary>
public sealed class PipeOAuth2TokenCache
{
    static private readonly TimeSpan Margin = TimeSpan.FromSeconds(60);
    static private readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public PipeOAuth2TokenCache(IHttpClientFactory httpClientFactory, TimeProvider time)
    {
        _httpClientFactory = httpClientFactory;
        _time = time;
    }

    private sealed class Entry
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public string? Token { get; set; }
        public DateTimeOffset Expires { get; set; }
    }

    public async Task<string> GetTokenAsync(string credentialId, OAuth2ClientCredentialsCredential credential, CancellationToken cancellationToken)
    {
        var entry = _entries.GetOrAdd(KeyOf(credentialId, credential), _ => new Entry());

        await entry.Lock.WaitAsync(cancellationToken);
        try
        {
            if (entry.Token is not null && entry.Expires > _time.GetUtcNow())
            {
                return entry.Token;
            }

            var (token, lifetime) = await FetchAsync(credential, cancellationToken);
            entry.Token = token;
            entry.Expires = _time.GetUtcNow() + (lifetime > Margin ? lifetime - Margin : TimeSpan.Zero);
            return token;
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    /// <summary>Drops the token of the credential, e.g. after a 401, so the next request fetches a new one.</summary>
    public void Invalidate(string credentialId, OAuth2ClientCredentialsCredential credential)
    {
        if (_entries.TryGetValue(KeyOf(credentialId, credential), out var entry))
        {
            entry.Token = null;
        }
    }

    // a changed credential gets its own entry; the secret only goes into a hash that stays in memory
    static private string KeyOf(string credentialId, OAuth2ClientCredentialsCredential c)
    {
        var settings = string.Join('\n', c.TokenUrl, c.ClientId, c.ClientSecret, c.Scope, c.Audience, c.ClientAuthentication);
        return credentialId + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(settings)));
    }

    private async Task<(string Token, TimeSpan Lifetime)> FetchAsync(OAuth2ClientCredentialsCredential credential, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(credential.TokenUrl, UriKind.Absolute, out var tokenUrl))
        {
            throw new PipeException($"The token URL '{credential.TokenUrl}' is not a valid address");
        }

        var form = new List<KeyValuePair<string, string>> { new("grant_type", "client_credentials") };
        if (!string.IsNullOrWhiteSpace(credential.Scope))
        {
            form.Add(new("scope", credential.Scope.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(credential.Audience))
        {
            form.Add(new("audience", credential.Audience.Trim()));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
        if (credential.ClientAuthentication == OAuth2ClientAuthentication.Body)
        {
            form.Add(new("client_id", credential.ClientId));
            form.Add(new("client_secret", credential.ClientSecret));
        }
        else
        {
            // RFC 6749 2.3.1: both parts are form-encoded before they are joined
            var user = Uri.EscapeDataString(credential.ClientId);
            var secret = Uri.EscapeDataString(credential.ClientSecret);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{secret}")));
        }
        request.Content = new FormUrlEncodedContent(form);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var json = TryParse(content);

        if (!response.IsSuccessStatusCode)
        {
            // status and the error of the provider; never the secret or a token
            var error = json?["error"]?.ToString();
            var description = json?["error_description"]?.ToString();
            var detail = string.Join(": ", new[] { error, description }.Where(s => !string.IsNullOrWhiteSpace(s)));
            throw new PipeException($"The token request failed with HTTP {(int)response.StatusCode}{(detail.Length > 0 ? $" ({Truncate(detail, 300)})" : "")}");
        }

        var token = json?["access_token"]?.ToString();
        if (string.IsNullOrEmpty(token))
        {
            throw new PipeException("The token endpoint answered without an access_token");
        }

        var lifetime = json?["expires_in"] is JsonValue seconds && seconds.TryGetValue<double>(out var s) && s > 0
            ? TimeSpan.FromSeconds(s)
            : json?["expires_in"]?.ToString() is { } text && double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                ? TimeSpan.FromSeconds(parsed)
                : DefaultLifetime;
        return (token, lifetime);
    }

    static private JsonNode? TryParse(string content)
    {
        try
        {
            return content.Length > 0 ? JsonNode.Parse(content) : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    static private string Truncate(string text, int length) => text.Length <= length ? text : text[..length] + "…";
}
