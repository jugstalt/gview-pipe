using System.Text.Json.Nodes;

namespace gView.Pipe.Abstraction.Data;

/// <summary>
/// The unit of data that flows along a connection: a JSON object plus optional binary attachments.
/// Nodes receive and emit lists of items.
/// </summary>
public sealed class PipeItem
{
    public PipeItem() : this(new JsonObject()) { }

    public PipeItem(JsonObject json)
    {
        Json = json;
    }

    public JsonObject Json { get; set; }

    /// <summary>Binary attachments by name. The bytes live in an <see cref="IPipeBinaryStore"/>.</summary>
    public Dictionary<string, PipeBinaryRef> Binary { get; set; } = new();

    public PipeItem Clone() => new((JsonObject)Json.DeepClone())
    {
        Binary = new Dictionary<string, PipeBinaryRef>(Binary)
    };
}
