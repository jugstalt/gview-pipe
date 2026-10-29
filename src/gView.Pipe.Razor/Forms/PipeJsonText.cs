using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Razor.Forms;

/// <summary>Conversions between JSON parameter values and the text shown in input fields.</summary>
static public class PipeJsonText
{
    static private readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Strings are shown raw, everything else as JSON.</summary>
    static public string ToText(JsonNode? value)
        => value switch
        {
            null => "",
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
            _ => value.ToJsonString()
        };

    static public string ToIndentedJson(JsonNode? value) => value?.ToJsonString(Indented) ?? "null";

    /// <summary>
    /// Text typed by the user to a JSON value: expressions and plain words stay strings,
    /// valid JSON (numbers, true, objects, …) becomes typed. Empty text is null.
    /// </summary>
    static public JsonNode? FromLooseText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        if (text.Contains("{{", StringComparison.Ordinal))
        {
            return JsonValue.Create(text);
        }

        try
        {
            return JsonNode.Parse(text) ?? JsonValue.Create(text);
        }
        catch (JsonException)
        {
            return JsonValue.Create(text);
        }
    }

    /// <summary>Numbers stay numbers; anything else (e.g. an expression) is kept as string.</summary>
    static public JsonNode? FromNumberText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return JsonValue.Create(number);
        }
        return JsonValue.Create(text);
    }
}
