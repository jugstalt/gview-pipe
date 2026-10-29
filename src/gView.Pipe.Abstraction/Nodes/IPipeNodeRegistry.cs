using gView.Pipe.Abstraction.Workflows;
using System.Text.Json.Nodes;

namespace gView.Pipe.Abstraction.Nodes;

public interface IPipeNodeRegistry
{
    IReadOnlyCollection<PipeNodeDescriptor> Nodes { get; }

    PipeNodeDescriptor? Find(Guid id);

    IReadOnlyList<PipePort> GetInputs(PipeNodeDescriptor descriptor, JsonObject parameters);

    /// <summary>Output ports, including the error port when <paramref name="settings"/> asks for it.</summary>
    IReadOnlyList<PipePort> GetOutputs(PipeNodeDescriptor descriptor, JsonObject parameters, PipeNodeSettings? settings = null);
}
