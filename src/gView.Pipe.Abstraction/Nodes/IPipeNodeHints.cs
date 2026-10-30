using System.Text.Json.Nodes;

namespace gView.Pipe.Abstraction.Nodes;

public enum PipeNodeHintLevel
{
    Info,
    Warning
}

public sealed record PipeNodeHint(PipeNodeHintLevel Level, string Message);

/// <summary>
/// Implemented by nodes that want to show notes in the editor depending on their parameters,
/// e.g. the URL of a webhook or a warning about a missing credential.
/// </summary>
public interface IPipeNodeHints
{
    IReadOnlyList<PipeNodeHint> GetHints(JsonObject parameters);
}
