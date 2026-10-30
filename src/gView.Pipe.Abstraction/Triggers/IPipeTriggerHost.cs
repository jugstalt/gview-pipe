namespace gView.Pipe.Abstraction.Triggers;

/// <summary>
/// Services the host offers to triggers, so trigger plugins don't need to know ASP.NET or timers.
/// </summary>
public interface IPipeTriggerHost
{
    /// <summary>
    /// Calls <paramref name="callback"/> on every occurrence of <paramref name="cronExpression"/>
    /// (5 fields, or 6 with seconds). Dispose the result to stop.
    /// </summary>
    IDisposable RegisterSchedule(string cronExpression, TimeZoneInfo timeZone, Func<CancellationToken, Task> callback);

    /// <summary>
    /// Routes HTTP requests for <see cref="PipeWebhookRegistration.Path"/> to the handler. Dispose the result to stop.
    /// </summary>
    /// <exception cref="PipeException">The path is invalid or already used by another webhook.</exception>
    IDisposable RegisterWebhook(PipeWebhookRegistration registration);
}
