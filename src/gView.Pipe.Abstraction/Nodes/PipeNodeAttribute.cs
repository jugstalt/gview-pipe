namespace gView.Pipe.Abstraction.Nodes;

/// <summary>
/// Registers a class implementing <see cref="IPipeNode"/> as a node type.
/// The <see cref="Id"/> is what workflows store, so it must never change once published.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PipeNodeAttribute : Attribute
{
    public PipeNodeAttribute(string id, string name)
    {
        Id = Guid.Parse(id);
        Name = name;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string? Description { get; set; }

    public string Category { get; set; } = "General";

    /// <summary>Current parameter version. Increase it together with <see cref="IPipeNodeMigration"/>.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Static input ports. Ignored for triggers (they have none) and nodes implementing <see cref="IPipeDynamicPorts"/>.</summary>
    public string[] Inputs { get; set; } = new[] { PipePorts.Main };

    /// <summary>Static output ports. Ignored for nodes implementing <see cref="IPipeDynamicPorts"/>.</summary>
    public string[] Outputs { get; set; } = new[] { PipePorts.Main };

    public bool Obsolete { get; set; }
}
