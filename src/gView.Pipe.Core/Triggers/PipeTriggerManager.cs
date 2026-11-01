using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Expressions;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Abstraction.Variables;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Expressions;
using gView.Pipe.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace gView.Pipe.Core.Triggers;

/// <summary>Arms the triggers of active workflows and starts executions when they fire.</summary>
internal sealed class PipeTriggerManager : IPipeTriggerManager, IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IPipeNodeRegistry _registry;
    private readonly IPipeTriggerHost _host;
    private readonly IPipeWorkflowRunner _runner;
    private readonly IPipeWorkflowStore _workflowStore;
    private readonly IPipeExpressionEvaluator _evaluator;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PipeCoreOptions _options;
    private readonly ILogger<PipeTriggerManager> _logger;
    private readonly ConcurrentDictionary<string, List<IAsyncDisposable>> _activations = new();

    public PipeTriggerManager(
        IServiceProvider services,
        IPipeNodeRegistry registry,
        IPipeTriggerHost host,
        IPipeWorkflowRunner runner,
        IPipeWorkflowStore workflowStore,
        IPipeExpressionEvaluator evaluator,
        ILoggerFactory loggerFactory,
        IOptions<PipeCoreOptions> options)
    {
        _services = services;
        _registry = registry;
        _host = host;
        _runner = runner;
        _workflowStore = workflowStore;
        _evaluator = evaluator;
        _loggerFactory = loggerFactory;
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<PipeTriggerManager>();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.ActivateTriggersOnStartup)
        {
            return;
        }

        foreach (var workflow in await _workflowStore.ListAsync(cancellationToken))
        {
            if (workflow.Active)
            {
                await ActivateAsync(workflow, cancellationToken);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var workflowId in _activations.Keys.ToList())
        {
            await DeactivateAsync(workflowId);
        }
    }

    public bool IsActive(string workflowId) => _activations.ContainsKey(workflowId);

    public async Task<IReadOnlyList<string>> ActivateAsync(PipeWorkflow workflow, CancellationToken cancellationToken = default)
    {
        await DeactivateAsync(workflow.Id);
        if (!workflow.Active)
        {
            return [];
        }

        var handles = new List<IAsyncDisposable>();
        var problems = new List<string>();

        foreach (var node in workflow.Nodes.Where(n => !n.Disabled))
        {
            if (_registry.Find(node.Type) is not { IsTrigger: true } descriptor)
            {
                continue;
            }

            try
            {
                var trigger = (IPipeTriggerNode)ActivatorUtilities.CreateInstance(_services, descriptor.NodeType);
                var context = new PipeTriggerContext(this, workflow, node, _loggerFactory.CreateLogger(descriptor.NodeType));
                if (await trigger.ActivateAsync(context, cancellationToken) is { } handle)
                {
                    handles.Add(handle);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Can't activate trigger {NodeName} of workflow {WorkflowName}", node.Name, workflow.Name);
                problems.Add($"{node.Name}: {ex.Message}");
            }
        }

        _activations[workflow.Id] = handles;
        _logger.LogInformation("Workflow {WorkflowName} activated with {TriggerCount} trigger(s)", workflow.Name, handles.Count);
        return problems;
    }

    public async Task DeactivateAsync(string workflowId)
    {
        if (!_activations.TryRemove(workflowId, out var handles))
        {
            return;
        }

        foreach (var handle in handles)
        {
            try
            {
                await handle.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Can't deactivate a trigger of workflow {WorkflowId}", workflowId);
            }
        }
    }

    private sealed class PipeTriggerContext : IPipeTriggerContext
    {
        private readonly PipeTriggerManager _manager;
        private readonly PipeWorkflow _workflow;
        private readonly PipeWorkflowNode _node;

        public PipeTriggerContext(PipeTriggerManager manager, PipeWorkflow workflow, PipeWorkflowNode node, ILogger logger)
        {
            _manager = manager;
            _workflow = workflow;
            _node = node;
            Logger = logger;
        }

        public string WorkflowId => _workflow.Id;
        public string WorkflowName => _workflow.Name;
        public string NodeId => _node.Id;
        public string NodeName => _node.Name;
        public IPipeTriggerHost Host => _manager._host;

        public IReadOnlyCollection<string> SelectableEnvironments
            => (_workflow.Settings.WebhookEnvironments ?? []).Select(PipeEnvironments.Normalize).ToList();
        public ILogger Logger { get; }

        public TParameters GetParameters<TParameters>() where TParameters : class, new()
            => PipeParameterResolver.Resolve<TParameters>(
                _node.Parameters,
                _manager._evaluator,
                new PipeExpressionScope { WorkflowId = _workflow.Id, WorkflowName = _workflow.Name });

        public async Task<PipeCredential> GetCredentialAsync(string credentialId, CancellationToken cancellationToken = default)
            => await _manager._services.GetRequiredService<IPipeCredentialStore>().GetAsync(credentialId, cancellationToken)
               ?? throw new PipeException($"Credential '{credentialId}' not found (trigger '{NodeName}')");

        public Task<PipeExecution?> FireAsync(IReadOnlyList<PipeItem> items, CancellationToken cancellationToken, PipeTriggerFireOptions? options = null)
        {
            var request = new PipeExecutionRequest
            {
                Mode = PipeExecutionMode.Trigger,
                StartNodeId = _node.Id,
                InputItems = items,
                ExecutionId = options?.ExecutionId,
                Environment = options?.Environment,
                BinaryContent = options?.BinaryContent
            };

            return _manager._runner.RunAsync(_workflow, request, cancellationToken);
        }
    }
}
