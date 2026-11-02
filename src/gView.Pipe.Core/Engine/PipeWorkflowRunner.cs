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
    private readonly PipeExecutionRetention _retention;
    private readonly PipeCoreOptions _options;
    private readonly ILogger<PipeWorkflowRunner> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _slots = new();
    // runs waiting per workflow, counted against MaxQueuedRuns; guarded by locking itself
    private readonly Dictionary<string, int> _queued = new();

    public PipeWorkflowRunner(
        IPipeEngine engine,
        IPipeWorkflowStore workflowStore,
        IPipeExecutionStore executionStore,
        IPipeNodeRegistry registry,
        PipeExecutionMonitor monitor,
        PipeExecutionRetention retention,
        IOptions<PipeCoreOptions> options,
        ILogger<PipeWorkflowRunner> logger)
    {
        _engine = engine;
        _workflowStore = workflowStore;
        _executionStore = executionStore;
        _registry = registry;
        _monitor = monitor;
        _retention = retention;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PipeExecution?> RunAsync(string workflowId, PipeExecutionRequest? request = null, CancellationToken cancellationToken = default)
    {
        var workflow = await _workflowStore.GetAsync(workflowId, cancellationToken)
            ?? throw new PipeException($"Workflow '{workflowId}' not found");

        return await RunAsync(workflow, request, cancellationToken);
    }

    public Task<PipeExecution?> RunAsync(PipeWorkflow workflow, PipeExecutionRequest? request = null, CancellationToken cancellationToken = default)
        => RunAsync(workflow, request, waitForSlot: false, cancellationToken);

    public async Task<PipeExecution> ReplayAsync(string workflowId, string executionId, string? fromNodeId = null, IPipeExecutionObserver? observer = null, CancellationToken cancellationToken = default)
    {
        var workflow = await _workflowStore.GetAsync(workflowId, cancellationToken)
            ?? throw new PipeException($"Workflow '{workflowId}' not found");
        var replayed = await _executionStore.GetAsync(workflowId, executionId, cancellationToken)
            ?? throw new PipeException($"Execution '{executionId}' not found");
        if (!replayed.DataSaved)
        {
            throw new PipeException("The execution was stored without item data and can't be replayed");
        }

        var request = new PipeExecutionRequest
        {
            Mode = PipeExecutionMode.Replay,
            Environment = replayed.Environment,
            ReplayOf = replayed,
            ReplayFromNodeId = fromNodeId,
            Observer = observer
        };
        return await RunAsync(workflow, request, waitForSlot: true, cancellationToken)
            ?? throw new OperationCanceledException("The replay was canceled while it waited for a free slot");
    }

    private async Task<PipeExecution?> RunAsync(PipeWorkflow workflow, PipeExecutionRequest? request, bool waitForSlot, CancellationToken cancellationToken)
    {
        var maxRuns = Math.Max(1, workflow.Settings.MaxConcurrentRuns);
        // keyed by limit as well, so a changed setting takes effect with the next run
        var slot = _slots.GetOrAdd($"{workflow.Id}:{maxRuns}", _ => new SemaphoreSlim(maxRuns, maxRuns));

        // the id is known before the run starts or waits, so it can be stopped from the UI (IPipeRunningExecutions.Cancel)
        request ??= new PipeExecutionRequest();
        if (request.ExecutionId is null)
        {
            request = request with { ExecutionId = Guid.NewGuid().ToString("N") };
        }
        var ticket = request.QueueTicket;

        if (!await slot.WaitAsync(0, cancellationToken))
        {
            // replays wait without limit; triggered runs only with room in the queue (ADR 0016)
            var queueLimit = waitForSlot ? (int?)null
                : request.Mode is PipeExecutionMode.Trigger or PipeExecutionMode.Error ? workflow.Settings.MaxQueuedRuns : 0;
            if (queueLimit <= 0)
            {
                _logger.LogWarning("Workflow {WorkflowName} already runs {MaxRuns} time(s); run skipped", workflow.Name, maxRuns);
                ticket?.Reject();
                return null;
            }
            if (!await WaitInQueueAsync(workflow, request, slot, queueLimit, cancellationToken))
            {
                ticket?.Reject();
                return null;
            }
        }

        // the caller may have abandoned the ticket just now; then the run must not start
        if (ticket is not null && !ticket.TryStart())
        {
            slot.Release();
            return null;
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
            await _retention.PruneAsync(workflow, CancellationToken.None);

            if (execution.Status == PipeExecutionStatus.Error && request.Mode is PipeExecutionMode.Trigger or PipeExecutionMode.SubWorkflow or PipeExecutionMode.Replay)
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
    /// Waits for a slot in the order of arrival (SemaphoreSlim queues async waiters FIFO). False when the queue is full,
    /// or when the run left the queue: canceled in the UI, abandoned by its trigger or by the caller.
    /// </summary>
    /// <param name="limit">Runs that may wait for this workflow; null = no limit (replays, not counted).</param>
    private async Task<bool> WaitInQueueAsync(PipeWorkflow workflow, PipeExecutionRequest request, SemaphoreSlim slot, int? limit, CancellationToken cancellationToken)
    {
        if (limit is not null)
        {
            lock (_queued)
            {
                var waiting = _queued.GetValueOrDefault(workflow.Id);
                if (waiting >= limit)
                {
                    _logger.LogWarning("Workflow {WorkflowName} is busy and {Waiting} run(s) already wait; run skipped", workflow.Name, waiting);
                    return false;
                }
                _queued[workflow.Id] = waiting + 1;
            }
        }

        var executionId = request.ExecutionId!;
        using var leave = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, request.QueueTicket?.AbandonedToken ?? CancellationToken.None);
        _monitor.RegisterQueued(executionId, workflow, request.Mode, leave);
        request.QueueTicket?.MarkQueued();
        _logger.LogInformation("Workflow {WorkflowName} is busy; run {ExecutionId} waits", workflow.Name, executionId);

        try
        {
            await slot.WaitAsync(leave.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Run {ExecutionId} of workflow {WorkflowName} left the queue before it started", executionId, workflow.Name);
            return false;
        }
        finally
        {
            _monitor.UnregisterQueued(executionId);
            if (limit is not null)
            {
                lock (_queued)
                {
                    _queued[workflow.Id]--;
                }
            }
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
