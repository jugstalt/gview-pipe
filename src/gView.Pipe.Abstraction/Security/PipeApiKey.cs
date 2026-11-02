using System.Security.Cryptography;
using System.Text;

namespace gView.Pipe.Abstraction.Security;

/// <summary>What an API key may do (ADR 0020).</summary>
[Flags]
public enum PipeApiRights
{
    None = 0,

    /// <summary>List and read workflows, executions and groups.</summary>
    Read = 1,

    /// <summary>Start, cancel and replay executions.</summary>
    Run = 2,

    /// <summary>Create, replace, delete, activate and deactivate workflows.</summary>
    Deploy = 4
}

/// <summary>A key for the REST API. Only the hash of the key is stored.</summary>
public sealed class PipeApiKey
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>SHA-256 of the key, hex.</summary>
    public string Hash { get; set; } = "";

    /// <summary>The first characters of the key, to tell keys apart in the list.</summary>
    public string Prefix { get; set; } = "";

    public PipeApiRights Rights { get; set; } = PipeApiRights.Read;

    /// <summary>Limits the key to workflows of these groups; null or empty: all workflows.</summary>
    public List<string>? Groups { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expires && expires <= now;

    /// <summary>True when the key may see the workflow of this group.</summary>
    public bool Covers(string? groupId) => Groups is not { Count: > 0 } || (groupId is not null && Groups.Contains(groupId));
}

static public class PipeApiKeys
{
    public const string Marker = "pipe_";

    /// <summary>A new key: <c>pipe_</c> plus 32 random bytes in Base64url; secret scanners recognize the prefix.</summary>
    static public string Generate()
        => Marker + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static public string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    /// <summary>Shown in lists: <c>pipe_AbC1…</c>.</summary>
    static public string PrefixOf(string key) => key[..Math.Min(key.Length, Marker.Length + 4)] + "…";
}

public interface IPipeApiKeyStore
{
    Task<IReadOnlyList<PipeApiKey>> ListAsync(CancellationToken cancellationToken = default);

    Task<PipeApiKey?> FindByHashAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>Saves the key. Assigns an id if it has none.</summary>
    Task SaveAsync(PipeApiKey key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
