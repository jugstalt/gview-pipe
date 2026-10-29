namespace gView.Pipe.Abstraction.Triggers;

/// <summary>
/// Services the host offers to triggers, so trigger plugins don't need to know ASP.NET or timers.
/// Webhook registration will be added here.
/// </summary>
public interface IPipeTriggerHost
{
    /// <summary>
    /// Calls <paramref name="callback"/> on every occurrence of <paramref name="cronExpression"/>
    /// (5 fields, or 6 with seconds). Dispose the result to stop.
    /// </summary>
    IDisposable RegisterSchedule(string cronExpression, TimeZoneInfo timeZone, Func<CancellationToken, Task> callback);
}
