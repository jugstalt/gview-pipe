using Blazor.Diagrams.Core;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Workflows;

namespace gView.Pipe.Razor.Editor;

/// <summary>Canvas model of a workflow node. Parameters and settings stay on <see cref="Node"/>.</summary>
public sealed class PipeNodeModel : NodeModel
{
    public const double Width = 210;
    public const double HeaderHeight = 52;
    public const double PortSpacing = 24;

    public PipeNodeModel(PipeWorkflowNode node, PipeNodeDescriptor? descriptor)
        : base(node.Id, new Point(node.Position.X, node.Position.Y))
    {
        Node = node;
        Descriptor = descriptor;
        Title = node.Name;
    }

    public PipeWorkflowNode Node { get; }

    /// <summary>Null when the node type is not installed.</summary>
    public PipeNodeDescriptor? Descriptor { get; }

    /// <summary>Run of the current or the inspected execution.</summary>
    public PipeNodeRun? Run { get; set; }

    /// <summary>The workflow has pin data for this node.</summary>
    public bool Pinned { get; set; }

    public IEnumerable<PipePortModel> InputPorts => Ports.OfType<PipePortModel>().Where(p => p.IsInput).OrderBy(p => p.Index);

    public IEnumerable<PipePortModel> OutputPorts => Ports.OfType<PipePortModel>().Where(p => !p.IsInput).OrderBy(p => p.Index);

    public double Height => HeaderHeight + Math.Max(1, Math.Max(InputPorts.Count(), OutputPorts.Count())) * PortSpacing + 8;

    public PipePortModel? FindPort(string name, bool isInput)
        => Ports.OfType<PipePortModel>().FirstOrDefault(p => p.IsInput == isInput && p.PortName == name);

    /// <summary>
    /// Makes the ports match <paramref name="inputs"/> and <paramref name="outputs"/>.
    /// Ports that disappear lose their links; ports in <paramref name="keep"/> stay even if not declared
    /// (e.g. ports of an unknown node type that are still connected).
    /// </summary>
    public void SyncPorts(Diagram diagram, IReadOnlyList<PipePort> inputs, IReadOnlyList<PipePort> outputs, IEnumerable<(string Name, bool IsInput)>? keep = null)
    {
        var wanted = inputs.Select(p => (p.Name, p.Label, IsInput: true))
            .Concat(outputs.Select(p => (p.Name, p.Label, IsInput: false)))
            .Concat((keep ?? []).Select(k => (k.Name, Label: (string?)null, k.IsInput)))
            .DistinctBy(p => (p.Name, p.IsInput))
            .ToList();

        foreach (var port in Ports.OfType<PipePortModel>().ToList())
        {
            if (!wanted.Any(w => w.Name == port.PortName && w.IsInput == port.IsInput))
            {
                foreach (var link in port.Links.ToList())
                {
                    diagram.Links.Remove(link);
                }
                RemovePort(port);
            }
        }

        foreach (var group in wanted.GroupBy(w => w.IsInput))
        {
            var index = 0;
            foreach (var (name, label, isInput) in group)
            {
                var port = FindPort(name, isInput) ?? (PipePortModel)AddPort(new PipePortModel(this, name, isInput));
                port.Label = label ?? name;
                port.Index = index++;
            }
        }

        Size = new Size(Width, Height);
        RefreshAll();
    }
}

public sealed class PipePortModel : PortModel
{
    /// <summary>Port circle as laid out by gview-pipe.css (.gview-pipe-port, .gview-pipe-node borders).</summary>
    public const double Diameter = 12;
    private const double BorderLeft = 1, BorderTop = 3, Overhang = 7;

    public PipePortModel(PipeNodeModel parent, string portName, bool isInput)
        : base(parent, isInput ? PortAlignment.Left : PortAlignment.Right)
    {
        PortName = portName;
        IsInput = isInput;
        Label = portName;

        // The diagram measures ports in the browser, asynchronously. If zoom, pan or the canvas position change
        // while a measurement is under way, the port ends up shifted and its links point into the void.
        // Our ports have a fixed place inside the node, so the measured position is replaced by the computed one.
        Changed += _ => SnapToNode();
    }

    public string PortName { get; }
    public bool IsInput { get; }
    public string Label { get; set; }
    public int Index { get; set; }

    /// <summary>Vertical center of the port inside the node widget.</summary>
    public double OffsetY => PipeNodeModel.HeaderHeight + Index * PipeNodeModel.PortSpacing + PipeNodeModel.PortSpacing / 2;

    /// <summary>Top left corner of the port circle in diagram coordinates.</summary>
    public Point ExpectedPosition => new(
        Parent.Position.X + (IsInput ? BorderLeft - Overhang : PipeNodeModel.Width - BorderLeft + Overhang - Diameter),
        Parent.Position.Y + BorderTop + OffsetY - Diameter / 2);

    private void SnapToNode()
    {
        if (Initialized)
        {
            Position = ExpectedPosition;
            Size = new Size(Diameter, Diameter);
        }
    }

    /// <summary>Links always connect an output with an input of another node, whichever end is dragged.</summary>
    public override bool CanAttachTo(ILinkable other)
        => other is PipePortModel port && port.Parent != Parent && port.IsInput != IsInput;
}
