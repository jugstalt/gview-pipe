using gView.Pipe.Abstraction.Execution;
using Microsoft.Extensions.Logging;

namespace gView.Pipe.Core.Engine;

/// <summary>Fans events out to all observers; a failing observer never breaks the execution.</summary>
internal sealed class PipeCompositeExecutionObserver : IPipeExecutionObserver
{
    private readonly IReadOnlyList<IPipeExecutionObserver> _observers;
    private readonly ILogger _logger;

    public PipeCompositeExecutionObserver(IEnumerable<IPipeExecutionObserver> observers, IPipeExecutionObserver? additional, ILogger logger)
    {
        _observers = additional is null ? observers.ToList() : [.. observers, additional];
        _logger = logger;
    }

    public Task OnExecutionStartedAsync(PipeExecution execution)
        => NotifyAsync(o => o.OnExecutionStartedAsync(execution));

    public Task OnNodeStartedAsync(PipeExecution execution, PipeNodeRun run)
        => NotifyAsync(o => o.OnNodeStartedAsync(execution, run));

    public Task OnNodeFinishedAsync(PipeExecution execution, PipeNodeRun run)
        => NotifyAsync(o => o.OnNodeFinishedAsync(execution, run));

    public Task OnExecutionFinishedAsync(PipeExecution execution)
        => NotifyAsync(o => o.OnExecutionFinishedAsync(execution));

    private async Task NotifyAsync(Func<IPipeExecutionObserver, Task> notify)
    {
        foreach (var observer in _observers)
        {
            try
            {
                await notify(observer);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Execution observer {Observer} failed", observer.GetType().Name);
            }
        }
    }
}
