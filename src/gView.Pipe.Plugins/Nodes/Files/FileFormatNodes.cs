using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Files;
using gView.Pipe.Abstraction.Nodes;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Files;

public enum ExtractOutput
{
    [PipeEnumValue("One item per row or object; the fields of the input item are not kept", Label = "One item per row")]
    Items,

    [PipeEnumValue("The input item stays one item and gets all rows or objects as a list in one field", Label = "All rows in one field")]
    Field
}

public sealed class ExtractFromFileNodeParameters
{
    [Required, PipeOptions, PipeParameter(Label = "Format", Order = 1, Expressions = false,
        Description = "Format of the file. Excel needs the Office plugin.")]
    public string Format { get; set; } = "csv";

    [PipeParameter(Label = "Output", Order = 2, Expressions = false, Description = "How the rows come out")]
    public ExtractOutput Output { get; set; } = ExtractOutput.Items;

    [PipeParameter(Label = "Field", Order = 3, Expressions = false, ShowWhen = nameof(Output), ShowWhenValues = ["field"],
        Description = "Name of the field that gets the list, e.g. rows")]
    public string Field { get; set; } = "rows";

    [PipeParameter(Label = "Header row", Order = 4, Expressions = false, ShowWhen = nameof(Format), ShowWhenValues = ["csv", "xlsx"],
        Description = "The first row holds the column names; otherwise the columns are column1, column2, …")]
    public bool Header { get; set; } = true;

    [PipeParameter(Label = "Delimiter", Order = 5, Expressions = false, Placeholder = "detect", ShowWhen = nameof(Format), ShowWhenValues = ["csv"],
        Description = @"Character between the columns, e.g. , or ; or \t; empty detects it from the first row")]
    public string? Delimiter { get; set; }

    [PipeParameter(Label = "Sheet", Order = 6, Placeholder = "first sheet", ShowWhen = nameof(Format), ShowWhenValues = ["xlsx"],
        Description = "Name of the sheet to read; empty reads the first")]
    public string? Sheet { get; set; }

    [PipeParameter(Label = "Item element", Order = 7, Expressions = false, Placeholder = "the whole document", ShowWhen = nameof(Format), ShowWhenValues = ["xml"],
        Description = "Every element of this name becomes an item, e.g. order; empty makes the whole document one item")]
    public string? ItemElement { get; set; }

    [PipeParameter(Label = "Always array", Order = 8, Expressions = false, Advanced = true, ShowWhen = nameof(Format), ShowWhenValues = ["xml"],
        Description = "Element names, separated by commas, that become lists even when they occur once, e.g. line")]
    public string? AlwaysArray { get; set; }

    [PipeParameter(Label = "Encoding", Order = 9, Expressions = false, Placeholder = "utf-8", Advanced = true, ShowWhen = nameof(Format), ShowWhenValues = ["csv", "json", "xml", "text"],
        Description = "Character set of the file, e.g. utf-8, windows-1252, iso-8859-1; a byte order mark wins")]
    public string? Encoding { get; set; }

    [PipeParameter(Label = "Attachment name", Order = 10, Expressions = false, Advanced = true,
        Description = "Name of the attachment to read; data unless an earlier node named it differently")]
    public string BinaryProperty { get; set; } = "data";
}

[PipeNode(PipeBuiltInNodes.ExtractFromFile, "Extract from File",
    Category = "Files",
    Description = "Turns an attached file (from a webhook, a mail, an HTTP response, Read File) into items: CSV, JSON, XML, text, "
                  + "and Excel with the Office plugin. Runs once per input item.")]
[PipeNodeExamples]
public sealed class ExtractFromFileNode : IPipeNode<ExtractFromFileNodeParameters>, IPipeParameterOptions
{
    private readonly IEnumerable<IPipeFileFormat> _formats;

    public ExtractFromFileNode(IEnumerable<IPipeFileFormat> formats)
    {
        _formats = formats;
    }

    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput();
        var output = new List<PipeItem>();

        for (var i = 0; i < items.Count; i++)
        {
            var parameters = context.GetParameters<ExtractFromFileNodeParameters>(items[i], i);
            var format = PipeFileFormatNodes.Find(_formats, parameters.Format);
            var reference = PipeFileFormatNodes.Attachment(items[i], parameters.BinaryProperty);

            IReadOnlyList<JsonObject> objects;
            await using (var content = await context.BinaryStore.OpenReadAsync(reference, cancellationToken))
            {
                objects = await format.ReadAsync(content, new PipeFileFormatOptions
                {
                    Header = parameters.Header,
                    Delimiter = parameters.Delimiter,
                    Encoding = parameters.Encoding,
                    Sheet = parameters.Sheet,
                    ItemElement = parameters.ItemElement,
                    AlwaysArray = PipeFileFormatNodes.List(parameters.AlwaysArray)
                }, cancellationToken);
            }

            if (parameters.Output == ExtractOutput.Field)
            {
                var result = items[i].Clone();
                result.Json[string.IsNullOrWhiteSpace(parameters.Field) ? "rows" : parameters.Field.Trim()]
                    = new JsonArray(objects.Select(o => (JsonNode)(o.Parent is null ? o : o.DeepClone())).ToArray());
                output.Add(result);
            }
            else
            {
                output.AddRange(objects.Select(o => new PipeItem(o.Parent is null ? o : (JsonObject)o.DeepClone())));
            }
        }

        return PipeNodeResult.FromMain(output);
    }

    public Task<IReadOnlyList<PipeParameterOption>> GetOptionsAsync(string parameter, JsonObject parameters, bool refresh, CancellationToken cancellationToken)
        => Task.FromResult(PipeFileFormatNodes.Options(_formats, parameter));
}

public enum ConvertMode
{
    [PipeEnumValue("One file with all items; CSV and Excel always do this", Label = "One file of all items")]
    AllItems,

    [PipeEnumValue("One file per item, attached to the item (JSON, XML, Text)", Label = "One file per item")]
    EachItem
}

public sealed class ConvertToFileNodeParameters
{
    [Required, PipeOptions, PipeParameter(Label = "Format", Order = 1, Expressions = false,
        Description = "Format of the file. Excel needs the Office plugin.")]
    public string Format { get; set; } = "csv";

    [PipeParameter(Label = "Files", Order = 2, Expressions = false, ShowWhen = nameof(Format), ShowWhenValues = ["json", "xml", "text"],
        Description = "One file of all items, or one file per item")]
    public ConvertMode Mode { get; set; } = ConvertMode.AllItems;

    [PipeParameter(Label = "File name", Order = 3, Placeholder = "data.csv",
        Description = "Name of the file, e.g. orders.csv; empty: data plus the extension of the format")]
    public string? FileName { get; set; }

    [PipeParameter(Label = "Header row", Order = 4, Expressions = false, ShowWhen = nameof(Format), ShowWhenValues = ["csv", "xlsx"],
        Description = "Write the column names as the first row")]
    public bool Header { get; set; } = true;

    [PipeParameter(Label = "Delimiter", Order = 5, Expressions = false, Placeholder = ",", ShowWhen = nameof(Format), ShowWhenValues = ["csv"],
        Description = @"Character between the columns, e.g. , or ; or \t")]
    public string? Delimiter { get; set; }

    [PipeParameter(Label = "Sheet", Order = 6, Placeholder = "Sheet1", ShowWhen = nameof(Format), ShowWhenValues = ["xlsx"],
        Description = "Name of the sheet, e.g. Orders")]
    public string? Sheet { get; set; }

    [PipeParameter(Label = "Sheet per field", Order = 7, Expressions = false, Advanced = true, ShowWhen = nameof(Format), ShowWhenValues = ["xlsx"],
        Description = "One sheet per value of this field, e.g. region makes a sheet per region")]
    public string? SheetPerField { get; set; }

    [PipeParameter(Label = "Root element", Order = 8, Placeholder = "items", ShowWhen = nameof(Format), ShowWhenValues = ["xml"],
        Description = "Name of the outer element, e.g. orders; for one file per item, the element of the item")]
    public string? RootElement { get; set; }

    [PipeParameter(Label = "Item element", Order = 9, Expressions = false, Placeholder = "item", ShowWhen = nameof(Format), ShowWhenValues = ["xml"],
        Description = "Element of each item in one file of all items, e.g. order")]
    public string? ItemElement { get; set; }

    [PipeParameter(Label = "Text field", Order = 10, Expressions = false, Placeholder = "text", ShowWhen = nameof(Format), ShowWhenValues = ["text"],
        Description = "Field of the items to write, one item per line, e.g. line")]
    public string? TextField { get; set; }

    [PipeParameter(Label = "Encoding", Order = 11, Expressions = false, Placeholder = "utf-8", Advanced = true, ShowWhen = nameof(Format), ShowWhenValues = ["csv", "json", "xml", "text"],
        Description = "Character set of the file, e.g. utf-8, windows-1252, iso-8859-1, utf-16")]
    public string? Encoding { get; set; }

    [PipeParameter(Label = "Attachment name", Order = 12, Expressions = false, Advanced = true,
        Description = "Name of the new attachment, under which later nodes find the file")]
    public string BinaryProperty { get; set; } = "data";
}

[PipeNodeExamples]
[PipeNode(PipeBuiltInNodes.ConvertToFile, "Convert to File",
    Category = "Files",
    Description = "Turns items into a file attachment, e.g. to send it by mail, upload it or save it with Write File: CSV, JSON, XML, text, "
                  + "and Excel with the Office plugin.")]
public sealed class ConvertToFileNode : IPipeNode<ConvertToFileNodeParameters>, IPipeParameterOptions
{
    private readonly IEnumerable<IPipeFileFormat> _formats;

    public ConvertToFileNode(IEnumerable<IPipeFileFormat> formats)
    {
        _formats = formats;
    }

    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput();
        if (items.Count == 0)
        {
            return PipeNodeResult.FromMain([]);
        }

        var first = context.GetParameters<ConvertToFileNodeParameters>(items[0]);
        var format = PipeFileFormatNodes.Find(_formats, first.Format);

        if (first.Mode == ConvertMode.AllItems || format.IsTable)
        {
            var item = new PipeItem(new JsonObject { ["format"] = format.Name, ["count"] = items.Count });
            await AttachAsync(context, format, first, items.Select(i => i.Json).ToList(), item, single: false, cancellationToken);
            return PipeNodeResult.FromMain([item]);
        }

        var output = new List<PipeItem>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var parameters = i == 0 ? first : context.GetParameters<ConvertToFileNodeParameters>(items[i], i);
            var result = items[i].Clone();
            await AttachAsync(context, format, parameters, [items[i].Json], result, single: true, cancellationToken);
            output.Add(result);
        }
        return PipeNodeResult.FromMain(output);
    }

    public Task<IReadOnlyList<PipeParameterOption>> GetOptionsAsync(string parameter, JsonObject parameters, bool refresh, CancellationToken cancellationToken)
        => Task.FromResult(PipeFileFormatNodes.Options(_formats, parameter));

    static private async Task AttachAsync(IPipeNodeContext context, IPipeFileFormat format, ConvertToFileNodeParameters parameters,
        IReadOnlyList<JsonObject> objects, PipeItem target, bool single, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await format.WriteAsync(buffer, objects, new PipeFileFormatOptions
        {
            Header = parameters.Header,
            Delimiter = parameters.Delimiter,
            Encoding = parameters.Encoding,
            Sheet = parameters.Sheet,
            SheetPerField = parameters.SheetPerField,
            RootElement = parameters.RootElement,
            ItemElement = parameters.ItemElement,
            TextField = parameters.TextField,
            SingleItem = single
        }, cancellationToken);

        buffer.Position = 0;
        var fileName = string.IsNullOrWhiteSpace(parameters.FileName) ? "data" + format.Extension : parameters.FileName.Trim();
        var property = string.IsNullOrWhiteSpace(parameters.BinaryProperty) ? "data" : parameters.BinaryProperty.Trim();
        target.Binary[property] = await context.BinaryStore.StoreAsync(buffer, fileName, format.MimeType, cancellationToken);
    }
}

static internal class PipeFileFormatNodes
{
    static public IPipeFileFormat Find(IEnumerable<IPipeFileFormat> formats, string? name)
        => formats.FirstOrDefault(f => f.Name.Equals(name?.Trim(), StringComparison.OrdinalIgnoreCase))
           ?? throw new PipeException($"Unknown format '{name}'. Available: {string.Join(", ", formats.Select(f => f.Name))}");

    static public PipeBinaryRef Attachment(PipeItem item, string? property)
    {
        var name = string.IsNullOrWhiteSpace(property) ? "data" : property.Trim();
        return item.Binary.TryGetValue(name, out var reference)
            ? reference
            : throw new PipeException($"The item has no attachment '{name}'" + (item.Binary.Count > 0 ? $"; it has: {string.Join(", ", item.Binary.Keys)}" : ""));
    }

    static public IReadOnlyList<string>? List(string? commaSeparated)
        => string.IsNullOrWhiteSpace(commaSeparated)
            ? null
            : commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    static public IReadOnlyList<PipeParameterOption> Options(IEnumerable<IPipeFileFormat> formats, string parameter)
        => parameter == "format"
            ? formats.OrderBy(f => f.Label).Select(f => new PipeParameterOption(f.Name, f.Label)).ToList()
            : [];
}
