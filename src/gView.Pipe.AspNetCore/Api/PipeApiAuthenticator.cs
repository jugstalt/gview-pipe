using gView.Pipe.Abstraction.Security;
using gView.Pipe.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace gView.Pipe.AspNetCore.Api;

/// <summary>The right an API endpoint needs (endpoint metadata, checked by <see cref="PipeApiAuthenticator"/>).</summary>
public sealed record PipeApiRightMetadata(PipeApiRights Right);

/// <summary>
/// Finds the API key of a request (<c>X-Pipe-Api-Key</c> or <c>Authorization: Bearer</c>) and checks it: known, not
/// expired, with the right of the endpoint. Too many failed attempts from one address answer 429 (ADR 0020).
/// </summary>
public sealed class PipeApiAuthenticator
{
    public const string HeaderName = "X-Pipe-Api-Key";
    public const int FailuresPerMinute = 60;

    static private readonly TimeSpan UsageWriteInterval = TimeSpan.FromMinutes(1);

    private readonly IPipeApiKeyStore _store;
    private readonly PipeAuditRefusals _refusals;
    private readonly TimeProvider _time;
    private readonly ILogger<PipeApiAuthenticator> _logger;
    private readonly ConcurrentDictionary<string, (DateTimeOffset Window, int Count)> _failures = new();

    public PipeApiAuthenticator(IPipeApiKeyStore store, PipeAuditRefusals refusals, TimeProvider time, ILogger<PipeApiAuthenticator> logger)
    {
        _store = store;
        _refusals = refusals;
        _time = time;
        _logger = logger;
    }

    /// <summary>The key of the request, or the problem to answer instead.</summary>
    public async Task<(PipeApiKey? Key, IResult? Problem)> AuthenticateAsync(HttpContext context, PipeApiRights right)
    {
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "";
        var now = _time.GetUtcNow();
        if (_failures.TryGetValue(address, out var failures) && now - failures.Window < TimeSpan.FromMinutes(1) && failures.Count >= FailuresPerMinute)
        {
            return (null, Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many failed attempts; wait a minute"));
        }

        var presented = KeyOf(context.Request);
        var key = string.IsNullOrEmpty(presented) ? null : await _store.FindByHashAsync(PipeApiKeys.Hash(presented), context.RequestAborted);
        if (key is null || key.IsExpired(now))
        {
            _failures.AddOrUpdate(address, _ => (now, 1), (_, f) => now - f.Window < TimeSpan.FromMinutes(1) ? (f.Window, f.Count + 1) : (now, 1));
            var reason = key is null ? (string.IsNullOrEmpty(presented) ? "no key" : "an unknown key") : $"the expired key '{key.Name}'";
            _logger.LogWarning("API request from {Address} with {Reason}", address, reason);
            await _refusals.RefusedAsync(address, "apiKeyRefused", reason);
            return (null, Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: key is null ? "A valid API key is needed (X-Pipe-Api-Key or Authorization: Bearer)" : "The API key has expired"));
        }

        if (!key.Rights.HasFlag(right))
        {
            return (null, Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: $"The API key '{key.Name}' lacks the right '{right.ToString().ToLowerInvariant()}'"));
        }

        if (key.LastUsedAt is not { } last || now - last > UsageWriteInterval)
        {
            // at most once a minute, so busy clients don't write the file on every request
            key.LastUsedAt = now;
            await _store.SaveAsync(key, CancellationToken.None);
        }
        return (key, null);
    }

    static private string? KeyOf(HttpRequest request)
    {
        if (request.Headers.TryGetValue(HeaderName, out var header) && !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString().Trim();
        }
        var authorization = request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..].Trim() : null;
    }
}
