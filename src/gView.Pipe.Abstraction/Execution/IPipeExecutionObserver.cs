namespace gView.Pipe.Abstraction.Execution;

/// <summary>
/// Receives progress events of executions. Register implementations in DI to observe all executions,
/// or pass one with <see cref="PipeExecutionRequest.Observer"/>. Exceptions thrown here are logged and ignored.
/// </summary>
public interface IPipeExecutionObserver
{
    Task OnExecutionStartedAsync(PipeExecution execution) => Task.CompletedTask;

    Task OnNodeStartedAsync(PipeExecution execution, PipeNodeRun run) => Task.CompletedTask;

    Task OnNodeFinishedAsync(PipeExecution execution, PipeNodeRun run) => Task.CompletedTask;

    /// <summary>
    /// A node wrote a log entry, while it runs (e.g. a line of a script's output). The entries are also in
    /// <see cref="PipeNodeRun.Logs"/> when the node has finished. Called in order, without waiting for the observer.
    /// </summary>
    Task OnNodeLogAsync(PipeExecution execution, PipeNodeRun run, PipeLogEntry entry) => Task.CompletedTask;

    Task OnExecutionFinishedAsync(PipeExecution execution) => Task.CompletedTask;
}
