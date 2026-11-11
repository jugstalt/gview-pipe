using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Abstraction.Nodes;

/// <summary>
/// The node has examples in the embedded resource <c>&lt;NodeClass&gt;.examples.json</c> next to the class,
/// or in the resource whose name ends with <see cref="ResourceName"/>. See ADR 0043.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PipeNodeExamplesAttribute : Attribute
{
    public PipeNodeExamplesAttribute(string? resourceName = null)
    {
        ResourceName = resourceName;
    }

    public string? ResourceName { get; }
}

/// <summary>A file attached to an example item: its name, type and text content; null for content that isn't text (e.g. a ZIP archive).</summary>
public sealed record PipeExampleFile(string FileName, string MimeType, string? Text);

/// <summary>An item of an example: the JSON and the attachments (written as <c>$binary</c> in the file).</summary>
public sealed class PipeExampleItem
{
    public const string BinaryKey = "$binary";

    public required JsonObject Json { get; init; }
    public IReadOnlyDictionary<string, PipeExampleFile> Binary { get; init; } = new Dictionary<string, PipeExampleFile>();
}

/// <summary>One example of a node: parameters, what it needs, input and output.</summary>
public sealed class PipeNodeExample
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public JsonObject Parameters { get; init; } = new();

    /// <summary>The world the example needs, e.g. <c>files</c> (path → text) in the folder <c>files</c>; other keys belong to plugins.</summary>
    public JsonObject? Given { get; init; }

    /// <summary>Input items per port (written as an array for <c>main</c>, or an object of arrays per port).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PipeExampleItem>> Input { get; init; } = new Dictionary<string, IReadOnlyList<PipeExampleItem>>();

    /// <summary>Output items per port.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PipeExampleItem>> Output { get; init; } = new Dictionary<string, IReadOnlyList<PipeExampleItem>>();

    /// <summary>Fields of output items whose value changes from run to run (dotted paths); not compared.</summary>
    public IReadOnlyList<string> Ignore { get; init; } = [];

    /// <summary>A test runs the example and compares the output; false: shown only (triggers, nodes without a fake).</summary>
    public bool Verified { get; init; } = true;

    /// <summary>
    /// The examples of a node class (<see cref="PipeNodeExamplesAttribute"/>); empty without the attribute.
    /// </summary>
    /// <exception cref="InvalidOperationException">The resource is missing or invalid.</exception>
    static public IReadOnlyList<PipeNodeExample> Load(Type nodeType)
    {
        if (nodeType.GetCustomAttribute<PipeNodeExamplesAttribute>() is not { } attribute)
        {
            return [];
        }

        var suffix = "." + (attribute.ResourceName ?? nodeType.Name + ".examples.json");
        var assembly = nodeType.Assembly;
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"Examples of {nodeType.Name}: no embedded resource ending with '{suffix}'");

        using var stream = assembly.GetManifestResourceStream(name)!;
        return Parse(JsonNode.Parse(stream, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }),
            nodeType.Name);
    }

    static public IReadOnlyList<PipeNodeExample> Parse(JsonNode? json, string source)
    {
        if (json is not JsonArray array)
        {
            throw new InvalidOperationException($"Examples of {source}: expected an array");
        }

        return array.Select((node, index) =>
        {
            if (node is not JsonObject example || example["title"]?.GetValue<string>() is not { Length: > 0 } title)
            {
                throw new InvalidOperationException($"Examples of {source}: example {index} needs a title");
            }

            return new PipeNodeExample
            {
                Title = title,
                Description = example["description"]?.GetValue<string>(),
                Parameters = example["parameters"] as JsonObject ?? new JsonObject(),
                Given = example["given"] as JsonObject,
                Input = Ports(example["input"], "input", source, title),
                Output = Ports(example["output"], "output", source, title),
                Ignore = example["ignore"] is JsonArray ignore ? ignore.Select(i => i!.GetValue<string>()).ToList() : [],
                Verified = example["verified"]?.GetValue<bool>() ?? true
            };
        }).ToList();
    }

    static private IReadOnlyDictionary<string, IReadOnlyList<PipeExampleItem>> Ports(JsonNode? node, string name, string source, string title)
        => node switch
        {
            null => new Dictionary<string, IReadOnlyList<PipeExampleItem>>(),
            JsonArray main => new Dictionary<string, IReadOnlyList<PipeExampleItem>> { [PipePorts.Main] = Items(main, source, title) },
            JsonObject ports => ports.ToDictionary(p => p.Key, p => Items(p.Value, source, title)),
            _ => throw new InvalidOperationException($"Examples of {source}, '{title}': {name} is an array or an object of arrays per port")
        };

    static private IReadOnlyList<PipeExampleItem> Items(JsonNode? node, string source, string title)
    {
        if (node is null)
        {
            return [];
        }
        if (node is not JsonArray items || items.Any(i => i is not JsonObject))
        {
            throw new InvalidOperationException($"Examples of {source}, '{title}': items are an array of objects");
        }

        return items.Select(i =>
        {
            var json = (JsonObject)i!.DeepClone();
            var binary = new Dictionary<string, PipeExampleFile>();
            if (json[PipeExampleItem.BinaryKey] is JsonObject files)
            {
                foreach (var (name, file) in files)
                {
                    var fileName = file?["fileName"]?.GetValue<string>() ?? name;
                    binary[name] = new PipeExampleFile(fileName, file?["mimeType"]?.GetValue<string>() ?? "application/octet-stream",
                        file?["text"]?.GetValue<string>());
                }
                json.Remove(PipeExampleItem.BinaryKey);
            }
            return new PipeExampleItem { Json = json, Binary = binary };
        }).ToList();
    }
}
