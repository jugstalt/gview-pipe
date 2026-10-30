namespace gView.Pipe.Abstraction.Nodes;

/// <summary>Metadata of a registered node type, built from <see cref="PipeNodeAttribute"/>.</summary>
public sealed class PipeNodeDescriptor
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Category { get; init; }
    public required int Version { get; init; }
    public required Type NodeType { get; init; }
    public Type? ParametersType { get; init; }
    public bool IsTrigger { get; init; }
    public bool HasDynamicPorts { get; init; }
    public bool HasHints { get; init; }
    public bool Obsolete { get; init; }
    public required IReadOnlyList<PipePort> Inputs { get; init; }
    public required IReadOnlyList<PipePort> Outputs { get; init; }

    /// <summary>Name of the plugin (assembly) the node comes from.</summary>
    public required string Source { get; init; }
}
