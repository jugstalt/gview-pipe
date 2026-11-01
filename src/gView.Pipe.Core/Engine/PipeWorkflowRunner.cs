using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Options;
using gView.Pipe.Abstraction.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Engine;

internal sealed class PipeWorkflowRunner : IPipeWorkflowRunner
{
    private readonly IPipeEngine _engine;
    private readonly IPipeWorkflowStore _workflowStore;
    private readonly IPipeExecutionStore _executionStore;
    private readonly IPipeNodeRegistry _registry;
    private readonly PipeExecutionMonitor _monitor;
    private readonly PipeCoreOptions _options;
    private readonly ILogger<PipeWorkflowRunner> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _slots = new();

    public PipeWorkflowRunner(
        IPipeEngine engine,
        IPipeWorkflowStore workflowStore,
        IPipeExecutionStore executionStore,
        IPipeNodeRegistry registry,
        PipeExecutionMonitor monitor,
        IOptions<PipeCoreOptions> options,
        ILogger<PipeWorkflowRunner> logger)
    {
        _engine = engine;
        _workflowStore = workflowStore;
        _executionStore = executionStore;
        _registry = registry;
        _monitor = monitor;
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

        // the id is known before the run starts, so the run can be stopped from the UI (IPipeRunningExecutions.Cancel)
        request ??= new PipeExecutionRequest();
        if (request.ExecutionId is null)
        {
            request = request with { ExecutionId = Guid.NewGuid().ToString("N") };
        }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitor.RegisterCancellation(request.ExecutionId!, cancellation);

        try
        {
            var execution = await _engine.ExecuteAsync(workflow, request, cancellation.Token);

            var toStore = execution;
            if (!workflow.Settings.SaveExecutionData)
            {
                toStore = PipeJson.Clone(execution);
                toStore.DataSaved = false;
                // counts and logs stay; logs are diagnostics, not item data
                foreach (var run in toStore.NodeRuns.Values)
                {
                    run.Inputs = null;
                    run.Outputs = null;
                }
            }

            // the history is written even if the caller canceled
            await _executionStore.SaveAsync(toStore, CancellationToken.None);
            await _executionStore.PruneAsync(workflow.Id, _options.ExecutionRetention, CancellationToken.None);

            if (execution.Status == PipeExecutionStatus.Error && request.Mode is PipeExecutionMode.Trigger or PipeExecutionMode.SubWorkflow)
            {
                await RunErrorWorkflowAsync(workflow, execution);
            }

            return execution;
        }
        finally
        {
            _monitor.UnregisterCancellation(request.ExecutionId!);
            slot.Release();
        }
    }

    /// <summary>
    /// Starts the workflow's error workflow for a failed triggered run. Problems are only logged:
    /// they must not hide the original failure.
    /// </summary>
    private async Task RunErrorWorkflowAsync(PipeWorkflow workflow, PipeExecution failed)
    {
        var errorWorkflowId = workflow.Settings.ErrorWorkflowId;
        if (string.IsNullOrEmpty(errorWorkflowId) || errorWorkflowId == workflow.Id)
        {
            return;
        }

        try
        {
            var errorWorkflow = await _workflowStore.GetAsync(errorWorkflowId, CancellationToken.None)
                ?? throw new PipeException($"Error workflow '{errorWorkflowId}' not found");
            var entry = PipeEntryNodes.Find<IPipeErrorTrigger>(errorWorkflow, _registry)
                ?? throw new PipeException($"Error workflow '{errorWorkflow.Name}' has no Error Trigger");

            var failedNode = failed.ErrorNodeId is { } nodeId ? failed.NodeRuns.GetValueOrDefault(nodeId) : null;
            var item = new PipeItem(new JsonObject
            {
                ["workflow"] = new JsonObject { ["id"] = workflow.Id, ["name"] = workflow.Name },
                ["execution"] = new JsonObject
                {
                    ["id"] = failed.Id,
                    ["mode"] = JsonSerializer.SerializeToNode(failed.Mode, PipeJson.Options),
                    ["startedAt"] = failed.StartedAt.ToString("O"),
                    ["error"] = failed.Error
                },
                ["node"] = failedNode is null ? null : new JsonObject { ["id"] = failedNode.NodeId, ["name"] = failedNode.NodeName, ["error"] = failedNode.Error }
            });

            await RunAsync(errorWorkflow, new PipeExecutionRequest
            {
                Mode = PipeExecutionMode.Error,
                Environment = failed.Environment,
                StartNodeId = entry.Id,
                InputItems = [item],
                ParentExecutionId = failed.Id,
                ParentWorkflowId = workflow.Id
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error workflow of {WorkflowName} could not be run", workflow.Name);
        }
    }
}
