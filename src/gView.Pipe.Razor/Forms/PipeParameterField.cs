using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Nodes;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Razor.Forms;

public enum PipeFieldKind
{
    Text,
    Number,
    Boolean,
    Enum,
    Json,
    Credential,
    Dictionary,
    List,
    RawJson
}

/// <summary>Form metadata of one property of a parameter or credential class.</summary>
public sealed class PipeParameterField
{
    /// <summary>Property name in the JSON (camelCase, as written by <c>PipeJson</c>).</summary>
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required PipeFieldKind Kind { get; init; }
    public string? Description { get; init; }
    public string? Placeholder { get; init; }
    public int Order { get; init; }
    public bool Multiline { get; init; }
    public bool Expressions { get; init; }
    public bool Required { get; init; }
    public bool Secret { get; init; }

    /// <summary>JSON names of the enum members (Kind = Enum).</summary>
    public IReadOnlyList<string> EnumValues { get; init; } = [];

    public IReadOnlyList<Guid> CredentialTypeIds { get; init; } = [];

    /// <summary>Kind of the values of a dictionary or list (Text or Json).</summary>
    public PipeFieldKind ItemKind { get; init; } = PipeFieldKind.Text;
}

static public class PipeParameterFields
{
    static private readonly ConcurrentDictionary<Type, IReadOnlyList<PipeParameterField>> Cache = new();

    static public IReadOnlyList<PipeParameterField> For(Type type) => Cache.GetOrAdd(type, Build);

    static private IReadOnlyList<PipeParameterField> Build(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Select((p, index) => Create(p, index))
            .OrderBy(f => f.Order)
            .ToList();

    static private PipeParameterField Create(PropertyInfo property, int index)
    {
        var parameter = property.GetCustomAttribute<PipeParameterAttribute>();
        var credential = property.GetCustomAttribute<PipeCredentialAttribute>();
        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        var kind = credential is not null ? PipeFieldKind.Credential : KindOf(propertyType);
        var itemKind = kind switch
        {
            PipeFieldKind.Dictionary => ValueKindOf(propertyType.GetGenericArguments()[1]),
            PipeFieldKind.List => ValueKindOf(ElementType(propertyType)!),
            _ => PipeFieldKind.Text
        };

        return new PipeParameterField
        {
            Key = JsonNamingPolicy.CamelCase.ConvertName(property.Name),
            Label = parameter?.Label ?? Humanize(property.Name),
            Kind = kind,
            ItemKind = itemKind,
            Description = parameter?.Description,
            Placeholder = parameter?.Placeholder,
            // properties without an explicit order keep their declaration order, after ordered ones
            Order = parameter?.Order is > 0 ? parameter.Order : 1000 + index,
            Multiline = parameter?.Multiline ?? false,
            Expressions = (parameter?.Expressions ?? true) && kind is not (PipeFieldKind.Boolean or PipeFieldKind.Enum or PipeFieldKind.Credential),
            Required = property.GetCustomAttribute<RequiredAttribute>() is not null,
            Secret = property.GetCustomAttribute<SecretAttribute>() is not null,
            EnumValues = propertyType.IsEnum
                ? Enum.GetNames(propertyType).Select(JsonNamingPolicy.CamelCase.ConvertName).ToList()
                : [],
            CredentialTypeIds = credential?.CredentialTypeIds ?? []
        };
    }

    static private PipeFieldKind KindOf(Type type)
    {
        if (type == typeof(string)) return PipeFieldKind.Text;
        if (type == typeof(bool)) return PipeFieldKind.Boolean;
        if (type.IsEnum) return PipeFieldKind.Enum;
        if (IsNumber(type)) return PipeFieldKind.Number;
        if (typeof(JsonNode).IsAssignableFrom(type) && type != typeof(JsonObject) && type != typeof(JsonArray)) return PipeFieldKind.Json;

        if (type.IsGenericType && type.GetGenericTypeDefinition() is var definition
            && (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
            && type.GetGenericArguments()[0] == typeof(string)
            && ValueKindOf(type.GetGenericArguments()[1]) != PipeFieldKind.RawJson)
        {
            return PipeFieldKind.Dictionary;
        }

        if (ElementType(type) is { } element && ValueKindOf(element) != PipeFieldKind.RawJson)
        {
            return PipeFieldKind.List;
        }

        return PipeFieldKind.RawJson;
    }

    /// <summary>Values of dictionaries and lists: plain text for strings, loose JSON for JsonNode and numbers.</summary>
    static private PipeFieldKind ValueKindOf(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string)) return PipeFieldKind.Text;
        if (type == typeof(JsonNode) || type == typeof(JsonValue) || IsNumber(type) || type == typeof(bool)) return PipeFieldKind.Json;
        return PipeFieldKind.RawJson;
    }

    static private Type? ElementType(Type type)
    {
        if (type.IsArray) return type.GetElementType();
        if (type.IsGenericType
            && type.GetGenericTypeDefinition() is var definition
            && (definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(IEnumerable<>)))
        {
            return type.GetGenericArguments()[0];
        }
        return null;
    }

    static private bool IsNumber(Type type)
        => type == typeof(int) || type == typeof(long) || type == typeof(short)
           || type == typeof(double) || type == typeof(float) || type == typeof(decimal);

    /// <summary>"RetryDelayMs" → "Retry delay ms".</summary>
    static public string Humanize(string name)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
            {
                builder.Append(' ');
                builder.Append(char.ToLowerInvariant(name[i]));
            }
            else
            {
                builder.Append(name[i]);
            }
        }
        return builder.ToString();
    }
}
