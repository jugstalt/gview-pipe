using Cronos;
using Microsoft.Extensions.Logging;

namespace gView.Pipe.Core.Triggers;

/// <summary>Cron schedules for triggers. Each registration runs its own delay loop.</summary>
internal sealed class PipeScheduler
{
    static private readonly TimeSpan MaxDelay = TimeSpan.FromDays(1);

    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PipeScheduler> _logger;

    public PipeScheduler(TimeProvider timeProvider, ILogger<PipeScheduler> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public IDisposable RegisterSchedule(string cronExpression, TimeZoneInfo timeZone, Func<CancellationToken, Task> callback)
    {
        var expression = Parse(cronExpression);
        var cts = new CancellationTokenSource();

        _ = RunAsync(cronExpression, expression, timeZone, callback, cts.Token);

        return new Registration(cts);
    }

    static public CronExpression Parse(string cronExpression)
    {
        var fields = cronExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return CronExpression.Parse(cronExpression, fields == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard);
    }

    private async Task RunAsync(string text, CronExpression expression, TimeZoneInfo timeZone, Func<CancellationToken, Task> callback, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var next = expression.GetNextOccurrence(_timeProvider.GetUtcNow(), timeZone);
                if (next is null)
                {
                    return;
                }

                // Task.Delay is limited to ~49 days, so wait in chunks
                TimeSpan remaining;
                while ((remaining = next.Value - _timeProvider.GetUtcNow()) > TimeSpan.Zero)
                {
                    await Task.Delay(remaining < MaxDelay ? remaining : MaxDelay, _timeProvider, cancellationToken);
                }

                try
                {
                    await callback(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Scheduled callback for '{Cron}' failed", text);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class Registration : IDisposable
    {
        private readonly CancellationTokenSource _cts;

        public Registration(CancellationTokenSource cts)
        {
            _cts = cts;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
