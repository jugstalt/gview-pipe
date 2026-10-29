using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Expressions;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Engine;

using PortItems = IReadOnlyDictionary<string, IReadOnlyList<PipeItem>>;

/// <summary>
/// Executes a workflow as a DAG. A node starts once all of its active (reachable) predecessors finished;
/// independent branches run in parallel. Nodes without input items are skipped unless they are start
/// nodes or have <see cref="PipeNodeSettings.AlwaysExecute"/>.
/// </summary>
internal sealed class PipeEngine : IPipeEngine
{
    static private readonly PortItems NoOutputs = new Dictionary<string, IReadOnlyList<PipeItem>>();

    private readonly IPipeNodeRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPipeExpressionEvaluator _evaluator;
    private readonly IEnumerable<IPipeExecutionObserver> _observers;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PipeCoreOptions _options;
    private readonly ILogger _logger;

    public PipeEngine(
        IPipeNodeRegistry registry,
        IServiceScopeFactory scopeFactory,
        IPipeExpressionEvaluator evaluator,
        IEnumerable<IPipeExecutionObserver> observers,
        ILoggerFactory loggerFactory,
        IOptions<PipeCoreOptions> options)
    {
        _registry = registry;
        _scopeFactory = scopeFactory;
        _evaluator = evaluator;
        _observers = observers;
        _loggerFactory = loggerFactory;
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<PipeEngine>();
    }

    public async Task<PipeExecution> ExecuteAsync(PipeWorkflow workflow, PipeExecutionRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new PipeExecutionRequest();

        var execution = new PipeExecution
        {
            Id = request.ExecutionId ?? Guid.NewGuid().ToString("N"),
            WorkflowId = workflow.Id,
            WorkflowName = workflow.Name,
            Mode = request.Mode,
            Status = PipeExecutionStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        var observer = new PipeCompositeExecutionObserver(_observers, request.Observer, _logger);

        await observer.OnExecutionStartedAsync(execution);

        try
        {
            var graph = PipeWorkflowGraph.Build(workflow);
            var startNodeIds = ResolveStartNodes(graph, request);

            await using var binaryStore = new PipeExecutionBinaryStore(_options.BinaryMemoryThreshold, _options.TempPath);
            await using var scope = _scopeFactory.CreateAsyncScope();

            var state = new PipeExecutionState(workflow, execution, graph, binaryStore, _evaluator);
            await RunGraphAsync(state, startNodeIds, request, scope.ServiceProvider, observer, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            execution.Status = PipeExecutionStatus.Canceled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execution {ExecutionId} of workflow {WorkflowName} failed", execution.Id, workflow.Name);
            execution.Status = PipeExecutionStatus.Error;
            execution.Error ??= ex.Message;
        }
        finally
        {
            execution.FinishedAt = DateTimeOffset.UtcNow;
        }

        await observer.OnExecutionFinishedAsync(execution);
        return execution;
    }

    static private HashSet<string> ResolveStartNodes(PipeWorkflowGraph graph, PipeExecutionRequest request)
    {
        if (request.StartNodeId is not null)
        {
            return graph.Nodes.ContainsKey(request.StartNodeId)
                ? [request.StartNodeId]
                : throw new PipeException($"Start node '{request.StartNodeId}' not found");
        }

        var roots = graph.Roots.ToHashSet();
        return roots.Count > 0 ? roots : throw new PipeException("Workflow has no start node");
    }

    private async Task RunGraphAsync(
        PipeExecutionState state,
        HashSet<string> startNodeIds,
        PipeExecutionRequest request,
        IServiceProvider services,
        IPipeExecutionObserver observer,
        CancellationToken cancellationToken)
    {
        var graph = state.Graph;
        var reachable = graph.ReachableFrom(startNodeIds);
        var pending = new HashSet<string>(reachable);
        var running = new Dictionary<Task<NodeOutcome>, string>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            while (pending.Count > 0 || running.Count > 0)
            {
                bool progressed;
                do
                {
                    progressed = false;
                    foreach (var nodeId in pending.ToList())
                    {
                        var predecessors = graph.Incoming[nodeId].Select(c => c.SourceNodeId).Where(reachable.Contains);
                        if (!predecessors.All(state.Outputs.ContainsKey))
                        {
                            continue;
                        }

                        pending.Remove(nodeId);
                        progressed = true;

                        var node = graph.Nodes[nodeId];
                        var isStart = startNodeIds.Contains(nodeId);
                        var inputs = isStart ? StartInputs(request) : CollectInputs(state, nodeId, reachable);
                        var run = new PipeNodeRun { NodeId = node.Id, NodeName = node.Name };
                        state.Execution.NodeRuns[nodeId] = run;

                        if (node.Disabled)
                        {
                            var passThrough = new Dictionary<string, IReadOnlyList<PipeItem>> { [PipePorts.Main] = inputs.Values.SelectMany(i => i).ToList() };
                            state.Outputs[nodeId] = passThrough;
                            await FinishRunAsync(state, run, PipeNodeRunStatus.Disabled, passThrough, observer);
                        }
                        else if (!isStart && !node.Settings.AlwaysExecute && inputs.Values.All(i => i.Count == 0))
                        {
                            state.Outputs[nodeId] = NoOutputs;
                            await FinishRunAsync(state, run, PipeNodeRunStatus.Skipped, NoOutputs, observer);
                        }
                        else
                        {
                            running.Add(RunNodeAsync(state, node, run, inputs, services, observer, cts.Token), nodeId);
                        }
                    }
                }
                while (progressed);

                if (running.Count == 0)
                {
                    if (pending.Count > 0)
                    {
                        throw new PipeException($"Nodes could not be scheduled: {string.Join(", ", pending)}");
                    }
                    break;
                }

                var finished = await Task.WhenAny(running.Keys);
                var finishedNodeId = running[finished];
                running.Remove(finished);

                var outcome = await finished;
                if (outcome.StopExecution)
                {
                    state.Execution.Status = PipeExecutionStatus.Error;
                    state.Execution.Error = $"{graph.Nodes[finishedNodeId].Name}: {outcome.Error}";
                    state.Execution.ErrorNodeId = finishedNodeId;
                    return;
                }

                state.Outputs[finishedNodeId] = outcome.Outputs;
            }

            state.Execution.Status = PipeExecutionStatus.Success;
        }
        finally
        {
            if (running.Count > 0)
            {
                await cts.CancelAsync();
                try
                {
                    await Task.WhenAll(running.Keys);
                }
                catch (OperationCanceledException) { }
            }

            foreach (var run in state.Execution.NodeRuns.Values.Where(r => r.Status is PipeNodeRunStatus.Running or PipeNodeRunStatus.Waiting))
            {
                run.Status = PipeNodeRunStatus.Canceled;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task<NodeOutcome> RunNodeAsync(
        PipeExecutionState state,
        PipeWorkflowNode node,
        PipeNodeRun run,
        PortItems inputs,
        IServiceProvider services,
        IPipeExecutionObserver observer,
        CancellationToken cancellationToken)
    {
        // let the scheduling loop continue before the node does any (possibly synchronous) work
        await Task.Yield();

        run.Status = PipeNodeRunStatus.Running;
        run.StartedAt = DateTimeOffset.UtcNow;
        await observer.OnNodeStartedAsync(state.Execution, run);

        var settings = node.Settings;
        Exception? lastError;

        try
        {
            var descriptor = _registry.Find(node.Type)
                ?? throw new PipeException($"Unknown node type {node.Type}. Is the plugin installed?");

            var attempt = 0;
            while (true)
            {
                run.Attempts = attempt + 1;
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (settings.TimeoutSeconds > 0)
                {
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
                }

                try
                {
                    var outputs = await ExecuteNodeAsync(state, node, descriptor, inputs, services, timeoutCts.Token);
                    await FinishRunAsync(state, run, PipeNodeRunStatus.Success, outputs, observer);
                    return new NodeOutcome(outputs, false, null);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    lastError = new TimeoutException($"Timed out after {settings.TimeoutSeconds}s");
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                if (++attempt > settings.RetryCount)
                {
                    break;
                }

                _logger.LogWarning("Node {NodeName} failed (attempt {Attempt}), retrying: {Error}", node.Name, attempt, lastError.Message);
                if (settings.RetryDelayMs > 0)
                {
                    await Task.Delay(settings.RetryDelayMs, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run.Status = PipeNodeRunStatus.Canceled;
            run.FinishedAt = DateTimeOffset.UtcNow;
            await observer.OnNodeFinishedAsync(state.Execution, run);
            throw;
        }
        catch (Exception ex)
        {
            lastError = ex;
        }

        return await HandleFailureAsync(state, node, run, inputs, lastError, observer);
    }

    private async Task<PortItems> ExecuteNodeAsync(
        PipeExecutionState state,
        PipeWorkflowNode node,
        PipeNodeDescriptor descriptor,
        PortItems inputs,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var instance = (IPipeNode)ActivatorUtilities.CreateInstance(services, descriptor.NodeType);
        try
        {
            var parameters = (JsonObject)node.Parameters.DeepClone();
            if (instance is IPipeNodeMigration migration && node.TypeVersion < descriptor.Version)
            {
                parameters = migration.MigrateParameters(node.TypeVersion, parameters);
            }

            var context = new PipeNodeContext(state, node, parameters, inputs, services, _loggerFactory.CreateLogger(descriptor.NodeType));
            var result = await instance.ExecuteAsync(context, cancellationToken);
            return result.Outputs;
        }
        finally
        {
            switch (instance)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }

    private async Task<NodeOutcome> HandleFailureAsync(
        PipeExecutionState state,
        PipeWorkflowNode node,
        PipeNodeRun run,
        PortItems inputs,
        Exception error,
        IPipeExecutionObserver observer)
    {
        _logger.LogWarning("Node {NodeName} failed: {Error}", node.Name, error.Message);
        run.Error = error.Message;

        var inputItems = inputs.TryGetValue(PipePorts.Main, out var main) ? main : inputs.Values.SelectMany(i => i).ToList();

        PortItems? outputs = node.Settings.OnError switch
        {
            PipeNodeErrorMode.Continue => new Dictionary<string, IReadOnlyList<PipeItem>> { [PipePorts.Main] = inputItems },
            PipeNodeErrorMode.ErrorOutput => new Dictionary<string, IReadOnlyList<PipeItem>> { [PipePorts.Error] = ToErrorItems(inputItems, error) },
            _ => null
        };

        await FinishRunAsync(state, run, PipeNodeRunStatus.Error, outputs ?? NoOutputs, observer);

        return outputs is null
            ? new NodeOutcome(NoOutputs, true, error.Message)
            : new NodeOutcome(outputs, false, error.Message);
    }

    static private List<PipeItem> ToErrorItems(IReadOnlyList<PipeItem> items, Exception error)
    {
        var errorItems = items.Count > 0 ? items.Select(i => i.Clone()).ToList() : [new PipeItem()];
        foreach (var item in errorItems)
        {
            item.Json["error"] = new JsonObject
            {
                ["message"] = error.Message,
                ["type"] = error.GetType().Name
            };
        }
        return errorItems;
    }

    static private async Task FinishRunAsync(PipeExecutionState state, PipeNodeRun run, PipeNodeRunStatus status, PortItems outputs, IPipeExecutionObserver observer)
    {
        run.Status = status;
        run.StartedAt ??= DateTimeOffset.UtcNow;
        run.FinishedAt = DateTimeOffset.UtcNow;
        run.Outputs = outputs.ToDictionary(p => p.Key, p => p.Value.ToList());
        run.OutputCounts = outputs.ToDictionary(p => p.Key, p => p.Value.Count);
        await observer.OnNodeFinishedAsync(state.Execution, run);
    }

    static private PortItems StartInputs(PipeExecutionRequest request)
        => new Dictionary<string, IReadOnlyList<PipeItem>>
        {
            [PipePorts.Main] = (request.InputItems ?? [new PipeItem()]).Select(i => i.Clone()).ToList()
        };

    /// <summary>
    /// Items of all incoming connections from active predecessors, grouped by target port.
    /// Items are cloned so branches can't affect each other.
    /// </summary>
    static private PortItems CollectInputs(PipeExecutionState state, string nodeId, HashSet<string> reachable)
    {
        var inputs = new Dictionary<string, List<PipeItem>>();

        foreach (var connection in state.Graph.Incoming[nodeId].Where(c => reachable.Contains(c.SourceNodeId)))
        {
            if (!inputs.TryGetValue(connection.TargetPort, out var items))
            {
                inputs[connection.TargetPort] = items = new List<PipeItem>();
            }

            if (state.Outputs[connection.SourceNodeId].TryGetValue(connection.SourcePort, out var sourceItems))
            {
                items.AddRange(sourceItems.Select(i => i.Clone()));
            }
        }

        return inputs.ToDictionary(p => p.Key, p => (IReadOnlyList<PipeItem>)p.Value);
    }

    private sealed record NodeOutcome(PortItems Outputs, bool StopExecution, string? Error);
}
