using gView.Pipe.Abstraction.Data;

namespace gView.Pipe.Abstraction;

/// <summary>
/// A node failed but has result items worth keeping (e.g. the output of scripts that ran). With
/// <c>OnError = Continue</c> or <c>ErrorOutput</c> the engine passes these items on instead of the node's input items.
/// </summary>
public class PipeNodeException : PipeException
{
    public PipeNodeException(string message, IReadOnlyList<PipeItem> items) : base(message)
    {
        Items = items;
    }

    public IReadOnlyList<PipeItem> Items { get; }
}
