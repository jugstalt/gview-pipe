using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Files;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Files;

public enum ReadFileFormat
{
    /// <summary>The file as binary attachment, plus file information.</summary>
    Binary,

    /// <summary>The content as text in the field <c>text</c>.</summary>
    Text,

    /// <summary>An object becomes one item, an array of objects one item per element.</summary>
    Json,

    /// <summary>One item per row; the first row holds the column names.</summary>
    Csv
}

public sealed class ReadFileNodeParameters
{
    [Required, PipeParameter(Label = "Folder", Order = 1, Expressions = false, Description = "Name of a folder released by the host")]
    public string Folder { get; set; } = "";

    [Required, PipeParameter(Label = "Path", Order = 2, Placeholder = "orders/*.csv",
        Description = "Relative to the folder; * and ? are allowed in the file name")]
    public string Path { get; set; } = "";

    [PipeParameter(Label = "Read as", Order = 3, Expressions = false)]
    public ReadFileFormat Format { get; set; } = ReadFileFormat.Binary;

    [PipeParameter(Label = "Include subfolders", Order = 4, Expressions = false, Description = "With wildcards: search below the path's folder too")]
    public bool IncludeSubfolders { get; set; }

    [PipeParameter(Label = "CSV delimiter", Order = 5, Expressions = false, Description = @"e.g. , or ; or \t")]
    public string CsvDelimiter { get; set; } = ",";

    [PipeParameter(Label = "Binary property", Order = 6, Expressions = false, Description = "Name of the attachment (binary format)")]
    public string BinaryProperty { get; set; } = "data";
}

[PipeNode(PipeBuiltInNodes.ReadFile, "Read File",
    Category = "Files",
    Description = "Reads files from a folder released by the host, as binary, text, JSON or CSV. Runs once per input item.")]
public sealed class ReadFileNode : IPipeNode<ReadFileNodeParameters>, IPipeNodeHints
{
    private readonly IPipeFileRoots _roots;

    public ReadFileNode(IPipeFileRoots roots)
    {
        _roots = roots;
    }

    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput();
        var output = new List<PipeItem>();

        for (var i = 0; i < items.Count; i++)
        {
            var parameters = context.GetParameters<ReadFileNodeParameters>(items[i], i);
            foreach (var file in FindFiles(parameters))
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.AddRange(await ReadAsync(context, parameters, file, cancellationToken));
            }
        }

        return PipeNodeResult.FromMain(output);
    }

    public IReadOnlyList<PipeNodeHint> GetHints(JsonObject parameters)
        => [PipeFileHints.Folders(_roots)];

    private IEnumerable<string> FindFiles(ReadFileNodeParameters parameters)
    {
        var path = _roots.Resolve(parameters.Folder, parameters.Path);
        var pattern = System.IO.Path.GetFileName(path);

        if (pattern.IndexOfAny(['*', '?']) < 0)
        {
            return File.Exists(path) ? [path] : throw new PipeException($"File '{parameters.Path}' not found in folder '{parameters.Folder}'");
        }

        // wildcards only in the file name; the folder part was checked by Resolve
        var directory = System.IO.Path.GetDirectoryName(path)!;
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, pattern, parameters.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Order()
            : [];
    }

    private async Task<List<PipeItem>> ReadAsync(IPipeNodeContext context, ReadFileNodeParameters parameters, string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        var fileJson = new JsonObject
        {
            ["fileName"] = info.Name,
            ["path"] = System.IO.Path.GetRelativePath(_roots.Resolve(parameters.Folder, ""), path).Replace('\\', '/'),
            ["size"] = info.Length,
            ["modifiedAt"] = info.LastWriteTimeUtc.ToString("O")
        };
        context.Logger.LogDebug("Reading {Path}", path);

        switch (parameters.Format)
        {
            case ReadFileFormat.Text:
                fileJson["text"] = await File.ReadAllTextAsync(path, cancellationToken);
                return [new PipeItem(fileJson)];

            case ReadFileFormat.Json:
                var node = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken));
                return node switch
                {
                    JsonObject obj => [new PipeItem(obj)],
                    // items are always objects: other values are wrapped
                    JsonArray array => array.Select(e => new PipeItem(e as JsonObject ?? new JsonObject { ["value"] = e?.DeepClone() })).ToList(),
                    _ => [new PipeItem(new JsonObject { ["value"] = node?.DeepClone() })]
                };

            case ReadFileFormat.Csv:
                var rows = PipeCsv.Parse(await File.ReadAllTextAsync(path, cancellationToken), PipeCsv.Delimiter(parameters.CsvDelimiter));
                return rows.Select(row => new PipeItem(row)).ToList();

            default:
                await using (var stream = File.OpenRead(path))
                {
                    var reference = await context.BinaryStore.StoreAsync(stream, info.Name, PipeMimeTypes.FromFileName(info.Name), cancellationToken);
                    var item = new PipeItem(fileJson);
                    item.Binary[string.IsNullOrWhiteSpace(parameters.BinaryProperty) ? "data" : parameters.BinaryProperty.Trim()] = reference;
                    return [item];
                }
        }
    }
}

static internal class PipeFileHints
{
    static public PipeNodeHint Folders(IPipeFileRoots roots)
        => roots.Names.Count == 0
            ? new(PipeNodeHintLevel.Warning, "The host has released no folders for workflows (PipeCoreOptions.FileRoots / Pipe:FileRoots).")
            : new(PipeNodeHintLevel.Info, $"Available folders: {string.Join(", ", roots.Names.Order())}");
}

static internal class PipeMimeTypes
{
    static private readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain", [".csv"] = "text/csv", [".json"] = "application/json", [".xml"] = "application/xml",
        [".html"] = "text/html", [".pdf"] = "application/pdf", [".png"] = "image/png", [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".zip"] = "application/zip"
    };

    static public string FromFileName(string fileName)
        => Known.GetValueOrDefault(Path.GetExtension(fileName)) ?? "application/octet-stream";
}
