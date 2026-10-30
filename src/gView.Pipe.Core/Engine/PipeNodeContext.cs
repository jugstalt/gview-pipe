using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Expressions;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Expressions;
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
}
