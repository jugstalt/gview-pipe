using System.Text.Json.Nodes;

namespace gView.Pipe.Abstraction.Nodes;

/// <summary>
/// Implemented by nodes whose ports depend on their parameters (e.g. a switch with N outputs).
/// </summary>
public interface IPipeDynamicPorts
{
    IReadOnlyList<PipePort> GetInputs(JsonObject parameters);

    IReadOnlyList<PipePort> GetOutputs(JsonObject parameters);
}
