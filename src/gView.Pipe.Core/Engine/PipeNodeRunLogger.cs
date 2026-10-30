using gView.Pipe.Abstraction.Execution;
using Microsoft.Extensions.Logging;

namespace gView.Pipe.Core.Engine;

/// <summary>
/// The logger a node gets: forwards to the application log and records the entries for the node run,
/// so they can be shown next to the node's input and output.
/// </summary>
internal sealed class PipeNodeRunLogger : ILogger
{
    private const int MaxEntries = 1000;
    private const int MaxMessageLength = 4000;

    private readonly ILogger _inner;
    private readonly Action<PipeLogEntry>? _live;
    private readonly List<PipeLogEntry> _entries = new();
    private bool _truncated;

    /// <param name="live">Gets every recorded entry right away (live output in the editor).</param>
    public PipeNodeRunLogger(ILogger inner, Action<PipeLogEntry>? live = null)
    {
        _inner = inner;
        _live = live;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);

    // the run log records Debug and above, whatever the application log level is
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug && logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (_inner.IsEnabled(logLevel))
        {
            _inner.Log(logLevel, eventId, state, exception, formatter);
        }

        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (exception is not null)
        {
            message = $"{message} ({exception.GetType().Name}: {exception.Message})";
        }
        if (message.Length > MaxMessageLength)
        {
            message = message[..MaxMessageLength] + "…";
        }

        var entry = new PipeLogEntry(DateTimeOffset.UtcNow, logLevel, message);
        // live: also beyond the recorded maximum, so a long running script stays visible
        _live?.Invoke(entry);

        lock (_entries)
        {
            if (_entries.Count < MaxEntries)
            {
                _entries.Add(entry);
            }
            else if (!_truncated)
            {
                _truncated = true;
                _entries.Add(new PipeLogEntry(DateTimeOffset.UtcNow, LogLevel.Warning, $"More than {MaxEntries} log entries; the rest is only in the application log"));
            }
        }
    }

    public List<PipeLogEntry> Snapshot()
    {
        lock (_entries)
        {
            return _entries.ToList();
        }
    }
}
