using gView.Pipe.Abstraction.Users;

namespace gView.Pipe.Abstraction.Audit;

/// <summary>What an audit entry is about (ADR 0022).</summary>
public enum PipeAuditArea
{
    Workflow,
    Group,
    Credential,
    Variables,
    ApiKey,
    Login,
    Execution
}

/// <summary>Who did what to which object, and when.</summary>
public sealed class PipeAuditEntry
{
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>User name, <c>api:&lt;key&gt;</c>, <c>anonymous</c> or <c>system</c>.</summary>
    public string Actor { get; set; } = "";

    public PipeAuditArea Area { get; set; }

    /// <summary>e.g. created, saved, deleted, activated, started, signedIn.</summary>
    public string Action { get; set; } = "";

    public string? ObjectId { get; set; }

    /// <summary>The name at the time of the action, so deleted objects can still be told apart.</summary>
    public string? ObjectName { get; set; }

    /// <summary>A short text, e.g. what changed in a workflow or the client address.</summary>
    public string? Details { get; set; }
}

/// <summary>Filters of <see cref="IPipeAuditLog.QueryAsync"/>; empty filters match everything.</summary>
public sealed record PipeAuditQuery
{
    public DateTimeOffset? From { get; init; }

    /// <summary>Before this time; pass the time of the last row to get the next page.</summary>
    public DateTimeOffset? Before { get; init; }

    /// <summary>Contained in the actor, ignoring case.</summary>
    public string? Actor { get; init; }

    public PipeAuditArea? Area { get; init; }
    public string? ObjectId { get; init; }
    public int MaxCount { get; init; } = 100;

    public bool Matches(PipeAuditEntry entry)
        => (From is null || entry.Timestamp >= From)
           && (Before is null || entry.Timestamp < Before)
           && (string.IsNullOrWhiteSpace(Actor) || entry.Actor.Contains(Actor.Trim(), StringComparison.OrdinalIgnoreCase))
           && (Area is null || entry.Area == Area)
           && (string.IsNullOrEmpty(ObjectId) || entry.ObjectId == ObjectId);
}

/// <summary>The audit log of the instance (ADR 0022). Entries are only appended.</summary>
public interface IPipeAuditLog
{
    Task WriteAsync(PipeAuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<PipeAuditEntry>> QueryAsync(PipeAuditQuery query, CancellationToken cancellationToken = default);

    /// <summary>Removes entries older than the given time (whole months in the file store).</summary>
    Task PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}

static public class PipeAuditExtensions
{
    public const string System = "system";

    /// <summary>Writes an entry for the current user of <paramref name="authorization"/>.</summary>
    static public async Task WriteAsync(this IPipeAuditLog log, IPipeAuthorization authorization, PipeAuditArea area, string action,
        string? objectId = null, string? objectName = null, string? details = null)
    {
        var roles = await authorization.GetRolesAsync();
        await log.WriteAsync(new PipeAuditEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Actor = roles.UserName ?? "anonymous",
            Area = area,
            Action = action,
            ObjectId = objectId,
            ObjectName = objectName,
            Details = details
        });
    }
}
