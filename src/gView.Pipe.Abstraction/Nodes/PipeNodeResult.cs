using gView.Pipe.Abstraction.Data;

namespace gView.Pipe.Abstraction.Nodes;

public sealed class PipeNodeResult
{
    public PipeNodeResult(IReadOnlyDictionary<string, IReadOnlyList<PipeItem>> outputs)
    {
        Outputs = outputs;
    }

    /// <summary>Output items per port name.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PipeItem>> Outputs { get; }

    public static PipeNodeResult Empty { get; } = new(new Dictionary<string, IReadOnlyList<PipeItem>>());

    public static PipeNodeResult FromMain(IEnumerable<PipeItem> items)
        => new(new Dictionary<string, IReadOnlyList<PipeItem>> { [PipePorts.Main] = items.ToList() });

    public static PipeNodeResult FromPorts(params (string Port, IEnumerable<PipeItem> Items)[] outputs)
        => new(outputs.ToDictionary(o => o.Port, o => (IReadOnlyList<PipeItem>)o.Items.ToList()));
}
