using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Files;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;
using gView.Pipe.Plugins.Common.Formats;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Files;

public enum ReadFileFormat
{
    [PipeEnumValue("The file as an attachment, plus its name, size and date; for PDFs, images, ZIP archives")]
    Binary,

    [PipeEnumValue("The content as text in the field text")]
    Text,

    [PipeEnumValue("Items from the JSON: an object becomes one item, an array one item per element", Label = "JSON")]
    Json,

    [PipeEnumValue("One item per row; the first row holds the column names", Label = "CSV")]
    Csv
}

public sealed class ReadFileNodeParameters
{
    [Required, PipeOptions, PipeParameter(Label = "Folder", Order = 1, Expressions = false,
        Description = "Where the files are: one of the folders the administrator has set up for workflows")]
    public string Folder { get; set; } = "";

    [Required, PipeParameter(Label = "Path", Order = 2, Placeholder = "orders/*.csv",
        Description = "File inside the folder, e.g. report.pdf; * and ? in the file name read all matching files, e.g. orders/*.csv")]
    public string Path { get; set; } = "";

    [PipeParameter(Label = "Read as", Order = 3, Expressions = false, Description = "What becomes of the file content")]
    public ReadFileFormat Format { get; set; } = ReadFileFormat.Binary;

    [PipeParameter(Label = "CSV delimiter", Order = 4, Expressions = false, ShowWhen = nameof(Format), ShowWhenValues = ["csv"],
        Description = @"Character between the columns, e.g. , or ; or \t for tab")]
    public string CsvDelimiter { get; set; } = ",";

    [PipeParameter(Label = "Include subfolders", Order = 5, Expressions = false, Advanced = true,
        Description = "Only with * or ? in the path: search the subfolders too, e.g. orders/*.csv then also finds orders/2025/a.csv")]
    public bool IncludeSubfolders { get; set; }

    [PipeParameter(Label = "Attachment name", Order = 6, Expressions = false, Advanced = true, ShowWhen = nameof(Format), ShowWhenValues = ["binary"],
        Description = "Name under which later nodes find the file, e.g. in Write File or Send Email")]
    public string BinaryProperty { get; set; } = "data";
}

[PipeNode(PipeBuiltInNodes.ReadFile, "Read File",
    Category = "Files",
    Description = "Reads files from a folder of the server: as an attachment, as text, or as items from JSON or CSV. Runs once per input item.")]
[PipeNodeExamples]
public sealed class ReadFileNode : IPipeNode<ReadFileNodeParameters>, IPipeNodeHints, IPipeParameterOptions
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
        => PipeFileHints.Folders(_roots);

    public Task<IReadOnlyList<PipeParameterOption>> GetOptionsAsync(string parameter, JsonObject parameters, bool refresh, CancellationToken cancellationToken)
        => Task.FromResult(PipeFileHints.FolderOptions(_roots, parameter));

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
            case ReadFileFormat.Csv:
                // the same formats as Extract from File (ADR 0018)
                IPipeFileFormat format = parameters.Format == ReadFileFormat.Json ? new PipeJsonFileFormat() : new PipeCsvFileFormat();
                await using (var stream = File.OpenRead(path))
                {
                    var objects = await format.ReadAsync(stream, new PipeFileFormatOptions { Delimiter = parameters.CsvDelimiter }, cancellationToken);
                    return objects.Select(o => new PipeItem(o)).ToList();
                }

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
    /// <summary>A warning when no folder is set up; the folders themselves are offered in the Folder field.</summary>
    static public IReadOnlyList<PipeNodeHint> Folders(IPipeFileRoots roots)
        => roots.Names.Count == 0
            ? [new(PipeNodeHintLevel.Warning, "No folders are set up for workflows yet: an administrator adds them in pipe.config (Pipe:FileRoots).")]
            : [];

    /// <summary>The folders set up for workflows, for the parameter <c>folder</c>.</summary>
    static public IReadOnlyList<PipeParameterOption> FolderOptions(IPipeFileRoots roots, string parameter)
        => parameter == "folder" ? roots.Names.Order().Select(n => new PipeParameterOption(n)).ToList() : [];
}

static internal class PipeMimeTypes
{
    static private readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain", [".csv"] = "text/csv", [".json"] = "application/json", [".xml"] = "application/xml",
        [".html"] = "text/html", [".pdf"] = "application/pdf", [".png"] = "image/png", [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".zip"] = "application/zip", [".gz"] = "application/gzip",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    static public string FromFileName(string fileName)
        => Known.GetValueOrDefault(Path.GetExtension(fileName)) ?? "application/octet-stream";
}
