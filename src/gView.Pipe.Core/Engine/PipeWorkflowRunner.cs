using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Options;
using gView.Pipe.Abstraction.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace gView.Pipe.Core.Engine;

internal sealed class PipeWorkflowRunner : IPipeWorkflowRunner
{
    private readonly IPipeEngine _engine;
    private readonly IPipeWorkflowStore _workflowStore;
    private readonly IPipeExecutionStore _executionStore;
    private readonly PipeCoreOptions _options;
    private readonly ILogger<PipeWorkflowRunner> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _slots = new();

    public PipeWorkflowRunner(
        IPipeEngine engine,
        IPipeWorkflowStore workflowStore,
        IPipeExecutionStore executionStore,
        IOptions<PipeCoreOptions> options,
        ILogger<PipeWorkflowRunner> logger)
    {
        _engine = engine;
        _workflowStore = workflowStore;
        _executionStore = executionStore;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PipeExecution?> RunAsync(string workflowId, PipeExecutionRequest? request = null, CancellationToken cancellationToken = default)
    {
        var workflow = await _workflowStore.GetAsync(workflowId, cancellationToken)
            ?? throw new PipeException($"Workflow '{workflowId}' not found");

        return await RunAsync(workflow, request, cancellationToken);
    }

    public async Task<PipeExecution?> RunAsync(PipeWorkflow workflow, PipeExecutionRequest? request = null, CancellationToken cancellationToken = default)
    {
        var maxRuns = Math.Max(1, workflow.Settings.MaxConcurrentRuns);
        // keyed by limit as well, so a changed setting takes effect with the next run
        var slot = _slots.GetOrAdd($"{workflow.Id}:{maxRuns}", _ => new SemaphoreSlim(maxRuns, maxRuns));

        if (!await slot.WaitAsync(0, cancellationToken))
        {
            _logger.LogWarning("Workflow {WorkflowName} already runs {MaxRuns} time(s); run skipped", workflow.Name, maxRuns);
            return null;
        }

        try
        {
            var execution = await _engine.ExecuteAsync(workflow, request, cancellationToken);

            var toStore = execution;
            if (!workflow.Settings.SaveExecutionData)
            {
                toStore = PipeJson.Clone(execution);
                toStore.DataSaved = false;
                foreach (var run in toStore.NodeRuns.Values)
                {
                    run.Outputs = null;
                }
            }

            // the history is written even if the caller canceled
            await _executionStore.SaveAsync(toStore, CancellationToken.None);
            await _executionStore.PruneAsync(workflow.Id, _options.ExecutionRetention, CancellationToken.None);

            return execution;
        }
        finally
        {
            slot.Release();
        }
    }
}
