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

    /// <summary>Editor notes of nodes implementing <see cref="IPipeNodeHints"/>; empty otherwise.</summary>
    IReadOnlyList<PipeNodeHint> GetHints(PipeNodeDescriptor descriptor, JsonObject parameters);

    /// <summary>Values for a parameter of nodes implementing <see cref="IPipeParameterOptions"/>; empty otherwise.</summary>
    Task<IReadOnlyList<PipeParameterOption>> GetParameterOptionsAsync(PipeNodeDescriptor descriptor, string parameter, JsonObject parameters,
        bool refresh = false, CancellationToken cancellationToken = default);
}
