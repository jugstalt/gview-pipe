using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace gView.Pipe.Core.Engine;

/// <summary>
/// Applies the retention of executions (count and age, per workflow or of the instance): after each stored
/// execution through the runner, and once a day for all workflows, so the age limit also applies to idle ones.
/// </summary>
internal sealed class PipeExecutionRetention : BackgroundService
{
    static public readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private readonly IPipeExecutionStore _executionStore;
    private readonly IPipeWorkflowStore _workflowStore;
    private readonly PipeCoreOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<PipeExecutionRetention> _logger;

    public PipeExecutionRetention(
        IPipeExecutionStore executionStore,
        IPipeWorkflowStore workflowStore,
        IOptions<PipeCoreOptions> options,
        TimeProvider time,
        ILogger<PipeExecutionRetention> logger)
    {
        _executionStore = executionStore;
        _workflowStore = workflowStore;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public Task PruneAsync(PipeWorkflow workflow, CancellationToken cancellationToken = default)
    {
        var keep = Math.Max(1, workflow.Settings.ExecutionRetention ?? _options.ExecutionRetention);
        var days = workflow.Settings.ExecutionRetentionDays ?? _options.ExecutionRetentionDays;
        DateTimeOffset? olderThan = days > 0 ? _time.GetUtcNow().AddDays(-days) : null;

        return _executionStore.PruneAsync(workflow.Id, keep, olderThan, cancellationToken);
    }

    public async Task PruneAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var workflow in await _workflowStore.ListAsync(cancellationToken))
        {
            try
            {
                await PruneAsync(workflow, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Pruning the executions of workflow {WorkflowName} failed", workflow.Name);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _time);
        do
        {
            await PruneAllAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
