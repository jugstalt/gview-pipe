using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Portal;
using System.Text.Json.Nodes;

namespace gView.Pipe.Abstraction.Triggers;

/// <summary>
/// Entry point of a workflow that portal users start: declares the form. Receives one item with the validated
/// values (ADR 0027).
/// </summary>
public interface IPipeFormTrigger : IPipeTriggerNode
{
    PipeForm GetForm(JsonObject parameters);
}

/// <summary>
/// A node whose single output item is a <see cref="PipeResultView"/> (as JSON): what the portal shows at the end
/// (ADR 0027).
/// </summary>
public interface IPipeResultNode : IPipeNode
{
}
