using gView.Pipe.Abstraction.Audit;
using System.Collections.Concurrent;

namespace gView.Pipe.AspNetCore.Authentication;

/// <summary>
/// Writes failed sign-ins and refused API keys to the audit log, at most one entry per minute, address and kind, so an
/// attack can't flood the log (ADR 0022). The entry counts the attempts of the minute before.
/// </summary>
public sealed class PipeAuditRefusals
{
    private readonly IPipeAuditLog? _log;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, (DateTimeOffset Since, int Suppressed)> _last = new();

    /// <param name="services">The audit log is optional: hosts without <c>AddPipeCore()</c> (e.g. tests) have none.</param>
    public PipeAuditRefusals(IServiceProvider services, TimeProvider time)
    {
        _log = services.GetService(typeof(IPipeAuditLog)) as IPipeAuditLog;
        _time = time;
    }

    /// <param name="action">e.g. <c>signInFailed</c>, <c>apiKeyRefused</c>.</param>
    public Task RefusedAsync(string address, string action, string? detail)
    {
        var now = _time.GetUtcNow();
        var key = $"{action}|{address}";
        var suppressed = 0;
        var write = false;
        _last.AddOrUpdate(key,
            _ =>
            {
                write = true;
                return (now, 0);
            },
            (_, last) =>
            {
                if (now - last.Since < TimeSpan.FromMinutes(1))
                {
                    return (last.Since, last.Suppressed + 1);
                }
                write = true;
                suppressed = last.Suppressed;
                return (now, 0);
            });
        if (!write || _log is null)
        {
            return Task.CompletedTask;
        }

        return _log.WriteAsync(new PipeAuditEntry
        {
            Timestamp = now,
            Actor = $"anonymous ({address})",
            Area = PipeAuditArea.Login,
            Action = action,
            Details = suppressed > 0 ? $"{detail}; {suppressed} more in the minute before" : detail
        });
    }
}
