using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Files;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Files;

public enum WriteFileContent
{
    /// <summary>One file per item: the item's attachment.</summary>
    Binary,

    /// <summary>One file per item: the <see cref="WriteFileNodeParameters.Text"/> parameter.</summary>
    Text,

    /// <summary>One file with all items as a JSON array.</summary>
    ItemsAsJson,

    /// <summary>One file with all items as CSV rows.</summary>
    ItemsAsCsv
}

public enum WriteFileIfExists
{
    Overwrite,
    Append,
    Fail
}

public sealed class WriteFileNodeParameters
{
    [Required, PipeParameter(Label = "Folder", Order = 1, Expressions = false, Description = "Name of a folder released by the host")]
    public string Folder { get; set; } = "";

    [Required, PipeParameter(Label = "Path", Order = 2, Placeholder = "export/{{ $json.id }}.pdf",
        Description = "Relative to the folder; missing subfolders are created")]
    public string Path { get; set; } = "";

    [PipeParameter(Label = "Write", Order = 3, Expressions = false)]
    public WriteFileContent Content { get; set; } = WriteFileContent.Binary;

    [PipeParameter(Label = "Binary property", Order = 4, Expressions = false, Description = "Attachment to write (binary)")]
    public string BinaryProperty { get; set; } = "data";

    [PipeParameter(Label = "Text", Order = 5, Multiline = true, Description = "Content to write (text)")]
    public string? Text { get; set; }

    [PipeParameter(Label = "If the file exists", Order = 6, Expressions = false,
        Description = "Append adds CSV rows without a header; it isn't possible for JSON")]
    public WriteFileIfExists IfExists { get; set; } = WriteFileIfExists.Overwrite;

    [PipeParameter(Label = "CSV delimiter", Order = 7, Expressions = false, Description = @"e.g. , or ; or \t")]
    public string CsvDelimiter { get; set; } = ",";
}

[PipeNode(PipeBuiltInNodes.WriteFile, "Write File",
    Category = "Files",
    Description = "Writes attachments or text (one file per item), or all items as JSON or CSV (one file), into a folder released by the host.")]
public sealed class WriteFileNode : IPipeNode<WriteFileNodeParameters>, IPipeNodeHints
{
    static private readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IPipeFileRoots _roots;

    public WriteFileNode(IPipeFileRoots roots)
    {
        _roots = roots;
    }

    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput();
        // all-items formats use the parameters of the first item (e.g. a path with a date)
        var first = context.GetParameters<WriteFileNodeParameters>(items.Count > 0 ? items[0] : null);

        if (first.Content is WriteFileContent.ItemsAsJson or WriteFileContent.ItemsAsCsv)
        {
            var info = await WriteAllItemsAsync(first, items, cancellationToken);
            context.Logger.LogInformation("Wrote {Count} items to {Path}", items.Count, info["path"]!.GetValue<string>());
            return PipeNodeResult.FromMain([new PipeItem(info)]);
        }

        var output = new List<PipeItem>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var parameters = i == 0 ? first : context.GetParameters<WriteFileNodeParameters>(items[i], i);
            var info = parameters.Content == WriteFileContent.Text
                ? await WriteBytesAsync(parameters, Utf8.GetBytes(parameters.Text ?? ""), cancellationToken)
                : await WriteBinaryAsync(context, parameters, items[i], cancellationToken);

            var result = items[i].Clone();
            result.Json["writtenFile"] = info;
            output.Add(result);
        }

        return PipeNodeResult.FromMain(output);
    }

    public IReadOnlyList<PipeNodeHint> GetHints(JsonObject parameters)
        => [PipeFileHints.Folders(_roots)];

    private async Task<JsonObject> WriteBinaryAsync(IPipeNodeContext context, WriteFileNodeParameters parameters, PipeItem item, CancellationToken cancellationToken)
    {
        var property = string.IsNullOrWhiteSpace(parameters.BinaryProperty) ? "data" : parameters.BinaryProperty.Trim();
        if (!item.Binary.TryGetValue(property, out var reference))
        {
            throw new PipeException($"The item has no attachment '{property}'");
        }

        await using var content = await context.BinaryStore.OpenReadAsync(reference, cancellationToken);
        return await WriteAsync(parameters, async stream => await content.CopyToAsync(stream, cancellationToken));
    }

    private async Task<JsonObject> WriteAllItemsAsync(WriteFileNodeParameters parameters, IReadOnlyList<PipeItem> items, CancellationToken cancellationToken)
    {
        if (parameters.Content == WriteFileContent.ItemsAsJson)
        {
            if (parameters.IfExists == WriteFileIfExists.Append)
            {
                throw new PipeException("Appending isn't possible for JSON; overwrite the file or write CSV");
            }
            var json = new JsonArray(items.Select(i => (JsonNode)i.Json.DeepClone()).ToArray())
                .ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            return await WriteBytesAsync(parameters, Utf8.GetBytes(json), cancellationToken);
        }

        // appending to an existing CSV file adds rows only
        var path = _roots.Resolve(parameters.Folder, parameters.Path);
        var header = !(parameters.IfExists == WriteFileIfExists.Append && File.Exists(path) && new FileInfo(path).Length > 0);
        var csv = PipeCsv.Write(items.Select(i => i.Json), PipeCsv.Delimiter(parameters.CsvDelimiter), header);
        return await WriteBytesAsync(parameters, Utf8.GetBytes(csv), cancellationToken);
    }

    private Task<JsonObject> WriteBytesAsync(WriteFileNodeParameters parameters, byte[] content, CancellationToken cancellationToken)
        => WriteAsync(parameters, async stream => await stream.WriteAsync(content, cancellationToken));

    private async Task<JsonObject> WriteAsync(WriteFileNodeParameters parameters, Func<Stream, Task> write)
    {
        var path = _roots.Resolve(parameters.Folder, parameters.Path);
        if (Directory.Exists(path))
        {
            throw new PipeException($"'{parameters.Path}' is a folder; the path needs a file name");
        }
        if (parameters.IfExists == WriteFileIfExists.Fail && File.Exists(path))
        {
            throw new PipeException($"File '{parameters.Path}' already exists in folder '{parameters.Folder}'");
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var mode = parameters.IfExists == WriteFileIfExists.Append ? FileMode.Append : FileMode.Create;
        await using (var stream = new FileStream(path, mode, FileAccess.Write, FileShare.None))
        {
            await write(stream);
        }

        return new JsonObject
        {
            ["folder"] = parameters.Folder,
            ["path"] = System.IO.Path.GetRelativePath(_roots.Resolve(parameters.Folder, ""), path).Replace('\\', '/'),
            ["size"] = new FileInfo(path).Length
        };
    }
}
