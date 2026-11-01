using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Abstraction.Expressions;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Variables;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Expressions;
using gView.Pipe.Core.Options;
using Microsoft.Extensions.Options;
using gView.Pipe.Abstraction.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Engine;

internal sealed class PipeNodeContext : IPipeNodeContext
{
    private readonly PipeExecutionState _state;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<PipeItem>> _inputs;

    public PipeNodeContext(
        PipeExecutionState state,
        PipeWorkflowNode node,
        JsonObject parameters,
        IReadOnlyDictionary<string, IReadOnlyList<PipeItem>> inputs,
        IServiceProvider services,
        ILogger logger)
    {
        _state = state;
        _inputs = inputs;
        NodeId = node.Id;
        NodeName = node.Name;
        RawParameters = parameters;
        Services = services;
        Logger = logger;
    }

    public string WorkflowId => _state.Workflow.Id;
    public string WorkflowName => _state.Workflow.Name;
    public string ExecutionId => _state.Execution.Id;
    public string Environment => _state.Execution.Environment;
    public PipeResolvedVariables Variables => _state.Variables;
    public string NodeId { get; }
    public string NodeName { get; }
    public JsonObject RawParameters { get; }
    public IReadOnlyCollection<string> InputPorts => _inputs.Keys.ToList();
    public IPipeBinaryStore BinaryStore => _state.BinaryStore;
    public ILogger Logger { get; }
    public IServiceProvider Services { get; }

    public IReadOnlyList<PipeItem> GetInput(string port = PipePorts.Main)
        => _inputs.TryGetValue(port, out var items) ? items : [];

    public TParameters GetParameters<TParameters>(PipeItem? item = null, int itemIndex = 0)
        where TParameters : class, new()
        => PipeParameterResolver.Resolve<TParameters>(RawParameters, _state.Evaluator, CreateExpressionScope(item, itemIndex));

    public JsonNode? Evaluate(string template, PipeItem? item = null, int itemIndex = 0)
        => _state.Evaluator.Evaluate(template, CreateExpressionScope(item, itemIndex));

    public PipeExpressionScope CreateExpressionScope(PipeItem? item = null, int itemIndex = 0)
        => _state.CreateScope(item, itemIndex, GetInput());

    public async Task<PipeCredential> GetCredentialAsync(string credentialId, CancellationToken cancellationToken = default)
    {
        var store = Services.GetRequiredService<IPipeCredentialStore>();
        return await store.GetAsync(credentialId, cancellationToken)
               ?? throw new PipeException($"Credential '{credentialId}' not found (node '{NodeName}')");
    }

    public async Task<TData> GetCredentialAsync<TData>(string credentialId, CancellationToken cancellationToken = default)
        where TData : class, new()
    {
        var credential = await GetCredentialAsync(credentialId, cancellationToken);
        return credential.Data.Deserialize<TData>(PipeJson.Options) ?? new TData();
    }

    public async Task<PipeExecution> ExecuteWorkflowAsync(string workflowId, IReadOnlyList<PipeItem> items, CancellationToken cancellationToken = default)
    {
        var execution = _state.Execution;
        var maxDepth = Services.GetRequiredService<IOptions<PipeCoreOptions>>().Value.MaxSubWorkflowDepth;
        if (execution.Depth >= maxDepth)
        {
            throw new PipeException($"Sub-workflows are nested more than {maxDepth} levels deep; is a workflow calling itself?");
        }

        var workflow = await Services.GetRequiredService<IPipeWorkflowStore>().GetAsync(workflowId, cancellationToken)
            ?? throw new PipeException($"Workflow '{workflowId}' not found");
        var entry = PipeEntryNodes.Find<IPipeSubWorkflowTrigger>(workflow, Services.GetRequiredService<IPipeNodeRegistry>())
            ?? throw new PipeException($"Workflow '{workflow.Name}' has no Execute Workflow Trigger");

        var request = new PipeExecutionRequest
        {
            Mode = PipeExecutionMode.SubWorkflow,
            // a test run must not reach production data through a sub-workflow
            Environment = execution.Environment,
            StartNodeId = entry.Id,
            InputItems = items,
            ParentExecutionId = execution.Id,
            ParentWorkflowId = execution.WorkflowId,
            Depth = execution.Depth + 1,
            BinaryStore = BinaryStore
        };

        return await Services.GetRequiredService<IPipeWorkflowRunner>().RunAsync(workflow, request, cancellationToken)
            ?? throw new PipeException($"Workflow '{workflow.Name}' is already running {workflow.Settings.MaxConcurrentRuns} time(s); raise its max concurrent runs to call it in parallel");
    }
}
