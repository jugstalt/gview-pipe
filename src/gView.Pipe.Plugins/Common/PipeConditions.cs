using gView.Pipe.Abstraction.Nodes;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Common;

public enum PipeConditionOperation
{
    [PipeEnumValue("Same value; numbers as numbers, text with case", Label = "is equal to")]
    IsEqual,

    [PipeEnumValue("Different value", Label = "is not equal to")]
    NotEqual,

    [PipeEnumValue("Text contains the text, or a list contains the value", Label = "contains")]
    Contains,

    [PipeEnumValue("Text or list doesn't contain it", Label = "doesn't contain")]
    NotContains,

    [PipeEnumValue("Numbers by value, text alphabetically", Label = "is greater than")]
    GreaterThan,

    [PipeEnumValue("Numbers by value, text alphabetically", Label = "is greater than or equal to")]
    GreaterThanOrEqual,

    [PipeEnumValue("Numbers by value, text alphabetically", Label = "is less than")]
    LessThan,

    [PipeEnumValue("Numbers by value, text alphabetically", Label = "is less than or equal to")]
    LessThanOrEqual,

    [PipeEnumValue("Missing, null, empty text or an empty list", Label = "is empty")]
    IsEmpty,

    [PipeEnumValue("Has a value", Label = "is not empty")]
    IsNotEmpty,

    [PipeEnumValue("true, a number other than 0 or a text like true or yes", Label = "is true")]
    IsTrue,

    [PipeEnumValue("Everything that isn't true", Label = "is false")]
    IsFalse
}

/// <summary>Parameters of a single comparison (used by If and Filter).</summary>
public class PipeConditionParameters
{
    [PipeParameter(Label = "Value 1", Order = 1, Description = "The value to check, usually a field such as {{ $json.total }}")]
    public JsonNode? Value1 { get; set; }

    [PipeParameter(Label = "Operation", Order = 2, Description = "How Value 1 is checked")]
    public PipeConditionOperation Operation { get; set; } = PipeConditionOperation.IsEqual;

    [PipeParameter(Label = "Value 2", Order = 3,
        ShowWhen = nameof(Operation), ShowWhenValues = ["isEqual", "notEqual", "contains", "notContains", "greaterThan", "greaterThanOrEqual", "lessThan", "lessThanOrEqual"],
        Description = "The value to compare with, e.g. 100 or {{ $json.limit }}")]
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
