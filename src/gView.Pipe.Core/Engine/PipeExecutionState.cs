using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Expressions;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Variables;
using gView.Pipe.Abstraction.Workflows;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Engine;

/// <summary>Shared state of one running execution.</summary>
internal sealed class PipeExecutionState
{
    private readonly Dictionary<string, string> _nodeIdsByName;

    public PipeExecutionState(
        PipeWorkflow workflow,
        PipeExecution execution,
        PipeWorkflowGraph graph,
        IPipeBinaryStore binaryStore,
        IPipeExpressionEvaluator evaluator,
        PipeResolvedVariables variables)
    {
        Variables = variables;
        Workflow = workflow;
        Execution = execution;
        Graph = graph;
        BinaryStore = binaryStore;
        Evaluator = evaluator;
        _nodeIdsByName = graph.Nodes.Values.ToDictionary(n => n.Name, n => n.Id);
    }

    public PipeWorkflow Workflow { get; }
    public PipeExecution Execution { get; }
    public PipeWorkflowGraph Graph { get; }
    public IPipeBinaryStore BinaryStore { get; }
    public IPipeExpressionEvaluator Evaluator { get; }
    public PipeResolvedVariables Variables { get; }

    /// <summary>Outputs of finished nodes: node id → port → items. Read concurrently by expressions.</summary>
    public ConcurrentDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<PipeItem>>> Outputs { get; } = new();

    public PipeExpressionScope CreateScope(PipeItem? item, int itemIndex, IReadOnlyList<PipeItem>? input = null) => new()
    {
        Json = item?.Json,
        ItemIndex = itemIndex,
        WorkflowId = Workflow.Id,
        WorkflowName = Workflow.Name,
        ExecutionId = Execution.Id,
        Environment = Execution.Environment,
        Variables = Variables.Values,
        Input = input?.Select(i => i.Json).ToList(),
        NodeItems = MainOutput
    };

    /// <summary>Main output of an executed node, by node name.</summary>
    private IReadOnlyList<JsonObject>? MainOutput(string nodeName)
        => _nodeIdsByName.TryGetValue(nodeName, out var id) && Outputs.TryGetValue(id, out var ports)
            ? ports.TryGetValue(PipePorts.Main, out var items) ? items.Select(i => i.Json).ToList() : []
            : null;
}
