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

    Task OnExecutionFinishedAsync(PipeExecution execution) => Task.CompletedTask;
}
