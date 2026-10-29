using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Common;

public enum PipeConditionOperation
{
    IsEqual,
    NotEqual,
    Contains,
    NotContains,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    IsEmpty,
    IsNotEmpty,
    IsTrue,
    IsFalse
}

/// <summary>Parameters of a single comparison (used by If and Filter).</summary>
public class PipeConditionParameters
{
    public JsonNode? Value1 { get; set; }
    public PipeConditionOperation Operation { get; set; } = PipeConditionOperation.IsEqual;
    public JsonNode? Value2 { get; set; }
}

static public class PipeConditions
{
    static public bool Evaluate(PipeConditionParameters condition)
        => Evaluate(condition.Value1, condition.Operation, condition.Value2);

    static public bool Evaluate(JsonNode? left, PipeConditionOperation operation, JsonNode? right)
        => operation switch
        {
            PipeConditionOperation.IsEqual => AreEqual(left, right),
            PipeConditionOperation.NotEqual => !AreEqual(left, right),
            PipeConditionOperation.Contains => Contains(left, right),
            PipeConditionOperation.NotContains => !Contains(left, right),
            PipeConditionOperation.GreaterThan => Compare(left, right) > 0,
            PipeConditionOperation.GreaterThanOrEqual => Compare(left, right) >= 0,
            PipeConditionOperation.LessThan => Compare(left, right) < 0,
            PipeConditionOperation.LessThanOrEqual => Compare(left, right) <= 0,
            PipeConditionOperation.IsEmpty => IsEmpty(left),
            PipeConditionOperation.IsNotEmpty => !IsEmpty(left),
            PipeConditionOperation.IsTrue => IsTruthy(left),
            PipeConditionOperation.IsFalse => !IsTruthy(left),
            _ => false
        };

    static public bool IsTruthy(JsonNode? value)
        => value switch
        {
            null => false,
            JsonArray array => array.Count > 0,
            JsonObject obj => obj.Count > 0,
            JsonValue v when v.GetValueKind() == JsonValueKind.True => true,
            JsonValue v when v.GetValueKind() == JsonValueKind.False => false,
            _ when TryGetNumber(value, out var number) => number != 0,
            _ => ToText(value) is { Length: > 0 } text && !text.Equals("false", StringComparison.OrdinalIgnoreCase)
        };

    static public string ToText(JsonNode? value)
        => value switch
        {
            null => "",
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
            _ => value.ToJsonString()
        };

    static public bool TryGetNumber(JsonNode? value, out double number)
    {
        number = 0;
        if (value is not JsonValue v)
        {
            return false;
        }

        return v.GetValueKind() switch
        {
            JsonValueKind.Number => double.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number),
            JsonValueKind.String => double.TryParse(v.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out number),
            _ => false
        };
    }

    static private bool AreEqual(JsonNode? left, JsonNode? right)
        => TryGetNumber(left, out var l) && TryGetNumber(right, out var r)
            ? l.Equals(r)
            : ToText(left) == ToText(right);

    static private bool Contains(JsonNode? left, JsonNode? right)
        => left is JsonArray array
            ? array.Any(item => AreEqual(item, right))
            : ToText(left).Contains(ToText(right), StringComparison.Ordinal);

    static private int Compare(JsonNode? left, JsonNode? right)
        => TryGetNumber(left, out var l) && TryGetNumber(right, out var r)
            ? l.CompareTo(r)
            : string.Compare(ToText(left), ToText(right), StringComparison.Ordinal);

    static private bool IsEmpty(JsonNode? value)
        => value switch
        {
            null => true,
            JsonArray array => array.Count == 0,
            JsonObject obj => obj.Count == 0,
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>().Length == 0,
            _ => false
        };
}
