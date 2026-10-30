namespace gView.Pipe.Razor.Components;

/// <summary>
/// Runs an action at most once per interval, however often it is triggered (e.g. re-rendering on every output line).
/// The action runs after the interval, so the last trigger is never lost.
/// </summary>
public sealed class PipeThrottle : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly Func<Task> _action;
    private int _pending;
    private bool _disposed;

    public PipeThrottle(TimeSpan interval, Func<Task> action)
    {
        _interval = interval;
        _action = action;
    }

    public void Trigger()
    {
        if (_disposed || Interlocked.Exchange(ref _pending, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(_interval);
            Interlocked.Exchange(ref _pending, 0);
            if (!_disposed)
            {
                try
                {
                    await _action();
                }
                catch (ObjectDisposedException) { } // the component went away meanwhile
            }
        });
    }

    public void Dispose() => _disposed = true;
}
