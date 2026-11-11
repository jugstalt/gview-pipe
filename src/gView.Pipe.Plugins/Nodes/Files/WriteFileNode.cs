using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Files;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common.Formats;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Files;

public enum WriteFileContent
{
    [PipeEnumValue("One file per item: the item's attachment, e.g. a PDF from Read File or a mail", Label = "Attachment")]
    Binary,

    [PipeEnumValue("One file per item with the text below")]
    Text,

    [PipeEnumValue("One file with all items as a JSON array", Label = "All items as JSON")]
    ItemsAsJson,

    [PipeEnumValue("One file with all items as CSV rows, one column per field", Label = "All items as CSV")]
    ItemsAsCsv
}

public enum WriteFileIfExists
{
    [PipeEnumValue("Replace the file")]
    Overwrite,

    [PipeEnumValue("Add to the end of the file; CSV adds rows without a second header, JSON can't be appended")]
    Append,

    [PipeEnumValue("Stop with an error and keep the file")]
    Fail
}

public sealed class WriteFileNodeParameters
{
    [Required, PipeOptions, PipeParameter(Label = "Folder", Order = 1, Expressions = false,
        Description = "Where to write: one of the folders the administrator has set up for workflows")]
    public string Folder { get; set; } = "";

    [Required, PipeParameter(Label = "Path", Order = 2, Placeholder = "export/{{ $json.id }}.pdf",
        Description = "File inside the folder, e.g. export/orders.csv; missing subfolders are created")]
    public string Path { get; set; } = "";

    [PipeParameter(Label = "Write", Order = 3, Expressions = false, Description = "What goes into the file")]
    public WriteFileContent Content { get; set; } = WriteFileContent.Binary;

    [PipeParameter(Label = "Text", Order = 4, Multiline = true, ShowWhen = nameof(Content), ShowWhenValues = ["text"],
        Description = "Content of the file, e.g. Order {{ $json.id }} received")]
    public string? Text { get; set; }

    [PipeParameter(Label = "CSV delimiter", Order = 5, Expressions = false, ShowWhen = nameof(Content), ShowWhenValues = ["itemsAsCsv"],
        Description = @"Character between the columns, e.g. , or ; or \t for tab")]
    public string CsvDelimiter { get; set; } = ",";

    [PipeParameter(Label = "If the file exists", Order = 6, Expressions = false, Description = "What happens when the file is already there")]
    public WriteFileIfExists IfExists { get; set; } = WriteFileIfExists.Overwrite;

    [PipeParameter(Label = "Attachment name", Order = 7, Expressions = false, Advanced = true, ShowWhen = nameof(Content), ShowWhenValues = ["binary"],
        Description = "Name of the attachment to write; data unless an earlier node named it differently")]
    public string BinaryProperty { get; set; } = "data";
}

[PipeNode(PipeBuiltInNodes.WriteFile, "Write File",
    Category = "Files",
    Description = "Writes files into a folder of the server: attachments or text (one file per item), or all items as JSON or CSV (one file).")]
[PipeNodeExamples]
public sealed class WriteFileNode : IPipeNode<WriteFileNodeParameters>, IPipeNodeHints, IPipeParameterOptions
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
        => PipeFileHints.Folders(_roots);

    public Task<IReadOnlyList<PipeParameterOption>> GetOptionsAsync(string parameter, JsonObject parameters, bool refresh, CancellationToken cancellationToken)
        => Task.FromResult(PipeFileHints.FolderOptions(_roots, parameter));

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
            return await WriteAsync(parameters, stream => new PipeJsonFileFormat().WriteAsync(
                stream, items.Select(i => i.Json).ToList(), new PipeFileFormatOptions(), cancellationToken));
        }

        // appending to an existing CSV file adds rows only
        var path = _roots.Resolve(parameters.Folder, parameters.Path);
        var header = !(parameters.IfExists == WriteFileIfExists.Append && File.Exists(path) && new FileInfo(path).Length > 0);
        return await WriteAsync(parameters, stream => new PipeCsvFileFormat().WriteAsync(
            stream, items.Select(i => i.Json).ToList(), new PipeFileFormatOptions { Delimiter = parameters.CsvDelimiter, Header = header }, cancellationToken));
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
