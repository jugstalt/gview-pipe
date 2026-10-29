namespace gView.Pipe.Abstraction.Nodes;

/// <summary>A named input or output of a node.</summary>
public sealed record PipePort(string Name, string? Label = null);

public static class PipePorts
{
    public const string Main = "main";

    /// <summary>Extra output that receives failed items when a node's error mode is <c>ErrorOutput</c>.</summary>
    public const string Error = "error";
}
