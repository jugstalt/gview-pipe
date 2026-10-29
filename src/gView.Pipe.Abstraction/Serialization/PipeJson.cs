using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace gView.Pipe.Core.Serialization;

/// <summary>The serializer settings used for workflows, executions, parameters and credentials.</summary>
static public class PipeJson
{
    static public JsonSerializerOptions Options { get; } = CreateOptions();

    static public JsonObject ToJsonObject(object? value)
        => value switch
        {
            null => new JsonObject(),
            JsonObject json => json,
            _ => JsonSerializer.SerializeToNode(value, Options) as JsonObject
                 ?? throw new ArgumentException($"{value.GetType().Name} does not serialize to a JSON object", nameof(value))
        };

    static public string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    static public T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    /// <summary>Deep copy through serialization, so stored objects don't share state with callers.</summary>
    static public T Clone<T>(T value) => Deserialize<T>(Serialize(value))!;

    static private JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
