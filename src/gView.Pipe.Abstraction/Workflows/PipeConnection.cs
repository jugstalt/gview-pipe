using gView.Pipe.Abstraction.Nodes;

namespace gView.Pipe.Abstraction.Workflows;

/// <summary>Connects an output port of one node with an input port of another.</summary>
public sealed class PipeConnection
{
    public string SourceNodeId { get; set; } = "";
    public string SourcePort { get; set; } = PipePorts.Main;
    public string TargetNodeId { get; set; } = "";
    public string TargetPort { get; set; } = PipePorts.Main;
}
