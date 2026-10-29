using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Abstraction.Serialization;

namespace gView.Pipe.Core.Workflows;

/// <summary>Builds workflows in code (tests, samples, programmatic workflows).</summary>
public sealed class PipeWorkflowBuilder
{
    private readonly PipeWorkflow _workflow;

    public PipeWorkflowBuilder(string name = "Workflow", string? id = null)
    {
        _workflow = new PipeWorkflow { Id = id ?? Guid.NewGuid().ToString("N"), Name = name };
    }

    /// <param name="id">Node id; also used as the node name.</param>
    /// <param name="type">Node type id (the <see cref="PipeNodeAttribute"/> id).</param>
    /// <param name="parameters">Parameter object or anonymous object, serialized to JSON.</param>
    public PipeWorkflowBuilder Node(string id, string type, object? parameters = null, Action<PipeWorkflowNode>? configure = null)
    {
        var node = new PipeWorkflowNode
        {
            Id = id,
            Name = id,
            Type = Guid.Parse(type),
            Parameters = PipeJson.ToJsonObject(parameters),
            Position = new PipePosition { X = _workflow.Nodes.Count * 220, Y = 0 }
        };
        configure?.Invoke(node);
        _workflow.Nodes.Add(node);
        return this;
    }

    public PipeWorkflowBuilder Connect(string sourceNodeId, string targetNodeId, string sourcePort = PipePorts.Main, string targetPort = PipePorts.Main)
    {
        _workflow.Connections.Add(new PipeConnection
        {
            SourceNodeId = sourceNodeId,
            SourcePort = sourcePort,
            TargetNodeId = targetNodeId,
            TargetPort = targetPort
        });
        return this;
    }

    public PipeWorkflowBuilder Configure(Action<PipeWorkflow> configure)
    {
        configure(_workflow);
        return this;
    }

    public PipeWorkflow Build() => _workflow;
}
