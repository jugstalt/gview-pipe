using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Files;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace gView.Pipe.Plugins.Common.Formats;

/// <summary>Text encodings by name (see ADR 0018); code pages like windows-1252 need the provider registered once.</summary>
static public class PipeTextEncodings
{
    static private readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    static PipeTextEncodings() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Null or empty: UTF-8 without BOM.</summary>
    static public Encoding Get(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Equals("utf-8", StringComparison.OrdinalIgnoreCase))
        {
            return Utf8;
        }
        try
        {
            return Encoding.GetEncoding(name.Trim());
        }
        catch (ArgumentException)
        {
            throw new PipeException($"Unknown encoding '{name}'; use e.g. utf-8, windows-1252, iso-8859-1 or utf-16");
        }
    }

    /// <summary>A BOM wins over the setting.</summary>
    static public async Task<string> ReadAsync(Stream content, string? encoding, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(content, Get(encoding), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    static public async Task WriteAsync(Stream target, string text, string? encoding, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(target, Get(encoding), leaveOpen: true);
        await writer.WriteAsync(text.AsMemory(), cancellationToken);
    }
}

public sealed class PipeCsvFileFormat : IPipeFileFormat
{
    public string Name => "csv";
    public string Label => "CSV";
    public string Extension => ".csv";
    public string MimeType => "text/csv";
    public bool IsTable => true;

    public async Task<IReadOnlyList<JsonObject>> ReadAsync(Stream content, PipeFileFormatOptions options, CancellationToken cancellationToken)
    {
        var text = await PipeTextEncodings.ReadAsync(content, options.Encoding, cancellationToken);
        var delimiter = string.IsNullOrEmpty(options.Delimiter) ? PipeCsv.DetectDelimiter(text) : PipeCsv.Delimiter(options.Delimiter);
        return PipeCsv.Parse(text, delimiter, options.Header);
    }

    public Task WriteAsync(Stream target, IReadOnlyList<JsonObject> items, PipeFileFormatOptions options, CancellationToken cancellationToken)
        => PipeTextEncodings.WriteAsync(target, PipeCsv.Write(items, PipeCsv.Delimiter(options.Delimiter), options.Header), options.Encoding, cancellationToken);
}

public sealed class PipeJsonFileFormat : IPipeFileFormat
{
    static private readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public string Name => "json";
    public string Label => "JSON";
    public string Extension => ".json";
    public string MimeType => "application/json";
    public bool IsTable => false;

    /// <summary>An object is one item, an array one item per element; other values are wrapped in <c>value</c>.</summary>
    static public IReadOnlyList<JsonObject> ToObjects(JsonNode? node) => node switch
    {
        JsonObject obj => [obj],
        JsonArray array => array.Select(e => e is JsonObject obj ? (JsonObject)obj.DeepClone() : new JsonObject { ["value"] = e?.DeepClone() }).ToList(),
        _ => [new JsonObject { ["value"] = node?.DeepClone() }]
    };

    public async Task<IReadOnlyList<JsonObject>> ReadAsync(Stream content, PipeFileFormatOptions options, CancellationToken cancellationToken)
    {
        var text = await PipeTextEncodings.ReadAsync(content, options.Encoding, cancellationToken);
        try
        {
            return ToObjects(JsonNode.Parse(text));
        }
        catch (JsonException ex)
        {
            throw new PipeException($"The content isn't valid JSON: {ex.Message}");
        }
    }

    public Task WriteAsync(Stream target, IReadOnlyList<JsonObject> items, PipeFileFormatOptions options, CancellationToken cancellationToken)
    {
        JsonNode node = options.SingleItem && items.Count == 1
            ? items[0].DeepClone()
            : new JsonArray(items.Select(i => (JsonNode)i.DeepClone()).ToArray());
        var json = options.Indent ? node.ToJsonString(Indented) : node.ToJsonString();
        return PipeTextEncodings.WriteAsync(target, json, options.Encoding, cancellationToken);
    }
}

public sealed class PipeTextFileFormat : IPipeFileFormat
{
    public string Name => "text";
    public string Label => "Text";
    public string Extension => ".txt";
    public string MimeType => "text/plain";
    public bool IsTable => false;

    public async Task<IReadOnlyList<JsonObject>> ReadAsync(Stream content, PipeFileFormatOptions options, CancellationToken cancellationToken)
        => [new JsonObject { ["text"] = await PipeTextEncodings.ReadAsync(content, options.Encoding, cancellationToken) }];

    /// <summary>The text field of each item, one per line.</summary>
    public Task WriteAsync(Stream target, IReadOnlyList<JsonObject> items, PipeFileFormatOptions options, CancellationToken cancellationToken)
    {
        var field = string.IsNullOrWhiteSpace(options.TextField) ? "text" : options.TextField.Trim();
        var text = string.Join("\n", items.Select(i => i[field] switch
        {
            null => "",
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
            var other => other.ToJsonString()
        }));
        return PipeTextEncodings.WriteAsync(target, text, options.Encoding, cancellationToken);
    }
}

/// <summary>
/// XML as JSON: attributes become <c>@name</c>, text next to attributes or elements <c>#text</c>, repeated elements arrays.
/// Namespace prefixes stay in the names; their declarations are kept as <c>@xmlns:prefix</c>.
/// </summary>
public sealed class PipeXmlFileFormat : IPipeFileFormat
{
    public string Name => "xml";
    public string Label => "XML";
    public string Extension => ".xml";
    public string MimeType => "application/xml";
    public bool IsTable => false;

    public async Task<IReadOnlyList<JsonObject>> ReadAsync(Stream content, PipeFileFormatOptions options, CancellationToken cancellationToken)
    {
        var text = await PipeTextEncodings.ReadAsync(content, options.Encoding, cancellationToken);
        XDocument document;
        try
        {
            // no DTDs: they could expand entities without limit
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new PipeException($"The content isn't valid XML: {ex.Message}");
        }

        var root = document.Root!;
        var alwaysArray = new HashSet<string>(options.AlwaysArray ?? [], StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(options.ItemElement))
        {
            var itemName = options.ItemElement.Trim();
            return root.DescendantsAndSelf()
                .Where(e => NameOf(e) == itemName)
                .Select(e => ToJson(e, alwaysArray) as JsonObject ?? new JsonObject { ["#text"] = e.Value })
                .ToList();
        }
        return [new JsonObject { [NameOf(root)] = ToJson(root, alwaysArray) }];
    }

    public Task WriteAsync(Stream target, IReadOnlyList<JsonObject> items, PipeFileFormatOptions options, CancellationToken cancellationToken)
    {
        XElement root;
        if (options.SingleItem && items.Count == 1 && string.IsNullOrWhiteSpace(options.RootElement)
            && items[0].Count == 1 && items[0].First().Value is JsonObject only)
        {
            // { "order": { … } } as read by Extract: the property is the root
            root = ToElement(items[0].First().Key, only, new Dictionary<string, XNamespace>());
        }
        else if (options.SingleItem && items.Count == 1)
        {
            root = ToElement(Or(options.RootElement, "item"), items[0], new Dictionary<string, XNamespace>());
        }
        else
        {
            root = new XElement(Or(options.RootElement, "items"));
            var itemName = Or(options.ItemElement, "item");
            foreach (var item in items)
            {
                root.Add(ToElement(itemName, item, new Dictionary<string, XNamespace>()));
            }
        }

        var settings = new XmlWriterSettings { Indent = options.Indent, Encoding = PipeTextEncodings.Get(options.Encoding), Async = true };
        return WriteDocumentAsync(target, new XDocument(new XDeclaration("1.0", settings.Encoding.WebName, null), root), settings, cancellationToken);
    }

    static private async Task WriteDocumentAsync(Stream target, XDocument document, XmlWriterSettings settings, CancellationToken cancellationToken)
    {
        await using var writer = XmlWriter.Create(target, settings);
        await document.SaveAsync(writer, cancellationToken);
    }

    static private string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    static private string NameOf(XElement element)
    {
        var prefix = element.GetPrefixOfNamespace(element.Name.Namespace);
        return string.IsNullOrEmpty(prefix) ? element.Name.LocalName : $"{prefix}:{element.Name.LocalName}";
    }

    static private string NameOf(XAttribute attribute)
    {
        if (attribute.IsNamespaceDeclaration)
        {
            return attribute.Name.Namespace == XNamespace.None ? "xmlns" : $"xmlns:{attribute.Name.LocalName}";
        }
        var prefix = attribute.Parent?.GetPrefixOfNamespace(attribute.Name.Namespace);
        return string.IsNullOrEmpty(prefix) ? attribute.Name.LocalName : $"{prefix}:{attribute.Name.LocalName}";
    }

    /// <summary>An element without attributes and child elements is its text; otherwise an object.</summary>
    static private JsonNode ToJson(XElement element, HashSet<string> alwaysArray)
    {
        var children = element.Elements().ToList();
        var text = string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
        if (!element.HasAttributes && children.Count == 0)
        {
            return JsonValue.Create(text)!;
        }

        var json = new JsonObject();
        foreach (var attribute in element.Attributes())
        {
            json["@" + NameOf(attribute)] = attribute.Value;
        }
        foreach (var child in children)
        {
            var name = NameOf(child);
            var value = ToJson(child, alwaysArray);
            switch (json[name])
            {
                case JsonArray array:
                    array.Add(value);
                    break;
                case JsonNode existing:
                    json[name] = new JsonArray(existing.DeepClone(), value);
                    break;
                default:
                    json[name] = alwaysArray.Contains(name) ? new JsonArray(value) : value;
                    break;
            }
        }
        if (text.Length > 0)
        {
            json["#text"] = text;
        }
        return json;
    }

    /// <param name="namespaces">Prefixes declared on the way down (<c>@xmlns:prefix</c>).</param>
    static private XElement ToElement(string name, JsonNode? value, Dictionary<string, XNamespace> namespaces)
    {
        if (value is not JsonObject obj)
        {
            return new XElement(XName(name, namespaces), ToText(value));
        }

        var scope = new Dictionary<string, XNamespace>(namespaces);
        foreach (var (key, declared) in obj.Where(p => p.Key == "@xmlns" || p.Key.StartsWith("@xmlns:", StringComparison.Ordinal)))
        {
            scope[key == "@xmlns" ? "" : key["@xmlns:".Length..]] = ToText(declared);
        }

        var element = new XElement(XName(name, scope));
        foreach (var (key, child) in obj)
        {
            if (key == "@xmlns")
            {
                continue;
            }
            if (key.StartsWith("@xmlns:", StringComparison.Ordinal))
            {
                element.Add(new XAttribute(XNamespace.Xmlns + key["@xmlns:".Length..], ToText(child)));
            }
            else if (key.StartsWith('@'))
            {
                element.Add(new XAttribute(XName(key[1..], scope, attribute: true), ToText(child)));
            }
            else if (key == "#text")
            {
                element.Add(new XText(ToText(child)));
            }
            else if (child is JsonArray array)
            {
                foreach (var entry in array)
                {
                    element.Add(ToElement(key, entry, scope));
                }
            }
            else
            {
                element.Add(ToElement(key, child, scope));
            }
        }
        return element;
    }

    /// <summary>A prefixed name in its declared namespace; names that aren't valid XML are encoded.</summary>
    static private XName XName(string name, Dictionary<string, XNamespace> scope, bool attribute = false)
    {
        var colon = name.IndexOf(':');
        if (colon > 0 && scope.TryGetValue(name[..colon], out var prefixed))
        {
            return prefixed + XmlConvert.EncodeLocalName(name[(colon + 1)..]);
        }
        var local = XmlConvert.EncodeLocalName(name);
        // unprefixed attributes are in no namespace, elements in the default one
        return !attribute && scope.TryGetValue("", out var defaultNamespace) ? defaultNamespace + local : local;
    }

    static private string ToText(JsonNode? value) => value switch
    {
        null => "",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        JsonValue v => v.ToJsonString(),
        _ => value.ToJsonString()
    };
}
