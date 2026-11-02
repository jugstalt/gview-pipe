using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Files;
using gView.Pipe.Abstraction.Nodes;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Files;

public enum ExtractOutput
{
    /// <summary>One item per row or object; the fields of the input item are not kept.</summary>
    Items,

    /// <summary>The input item with all rows or objects in one field.</summary>
    Field
}

public sealed class ExtractFromFileNodeParameters
{
    [Required, PipeOptions, PipeParameter(Label = "Format", Order = 1, Expressions = false,
        Description = "Format of the file. Excel needs the Office plugin.")]
    public string Format { get; set; } = "csv";

    [PipeParameter(Label = "Binary property", Order = 2, Expressions = false, Description = "Attachment to read")]
    public string BinaryProperty { get; set; } = "data";

    [PipeParameter(Label = "Output", Order = 3, Expressions = false,
        Description = "Items: one item per row or object. Field: the input item with all of them in one field.")]
    public ExtractOutput Output { get; set; } = ExtractOutput.Items;

    [PipeParameter(Label = "Field", Order = 4, Expressions = false, Description = "Output 'Field': name of the field")]
    public string Field { get; set; } = "rows";

    [PipeParameter(Label = "Header row", Order = 5, Expressions = false,
        Description = "CSV, Excel: the first row holds the column names; otherwise the columns are column1, column2, …")]
    public bool Header { get; set; } = true;

    [PipeParameter(Label = "Delimiter", Order = 6, Expressions = false, Placeholder = "detect",
        Description = @"CSV: e.g. , or ; or \t; empty detects it from the first row")]
    public string? Delimiter { get; set; }

    [PipeParameter(Label = "Encoding", Order = 7, Expressions = false, Placeholder = "utf-8",
        Description = "CSV, JSON, XML, Text: e.g. utf-8, windows-1252, iso-8859-1, utf-16; a byte order mark wins")]
    public string? Encoding { get; set; }

    [PipeParameter(Label = "Sheet", Order = 8, Placeholder = "first sheet", Description = "Excel: name of the sheet to read")]
    public string? Sheet { get; set; }

    [PipeParameter(Label = "Item element", Order = 9, Expressions = false, Placeholder = "the whole document",
        Description = "XML: every element of this name becomes an item, e.g. order")]
    public string? ItemElement { get; set; }

    [PipeParameter(Label = "Always array", Order = 10, Expressions = false,
        Description = "XML: element names, separated by commas, read as arrays even when they occur once")]
    public string? AlwaysArray { get; set; }
}

[PipeNode(PipeBuiltInNodes.ExtractFromFile, "Extract from File",
    Category = "Files",
    Description = "Turns an attachment (a file from a webhook, a mail, an HTTP response, Read File) into items: CSV, JSON, XML, text, "
                  + "and Excel with the Office plugin. Runs once per input item.")]
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
    /// <summary>All items into one file; tables always do this.</summary>
    AllItems,

    /// <summary>One file per item (JSON, XML, Text).</summary>
    EachItem
}

public sealed class ConvertToFileNodeParameters
{
    [Required, PipeOptions, PipeParameter(Label = "Format", Order = 1, Expressions = false,
        Description = "Format of the file. Excel needs the Office plugin.")]
    public string Format { get; set; } = "csv";

    [PipeParameter(Label = "Files", Order = 2, Expressions = false,
        Description = "All items into one file, or one file per item (JSON, XML, Text). CSV and Excel always make one file.")]
    public ConvertMode Mode { get; set; } = ConvertMode.AllItems;

    [PipeParameter(Label = "File name", Order = 3, Placeholder = "data.csv",
        Description = "Name of the file; empty: data plus the format's extension. One file of all items uses the first item for expressions.")]
    public string? FileName { get; set; }

    [PipeParameter(Label = "Binary property", Order = 4, Expressions = false, Description = "Attachment to write")]
    public string BinaryProperty { get; set; } = "data";

    [PipeParameter(Label = "Header row", Order = 5, Expressions = false, Description = "CSV, Excel: write the column names as the first row")]
    public bool Header { get; set; } = true;

    [PipeParameter(Label = "Delimiter", Order = 6, Expressions = false, Placeholder = ",", Description = @"CSV: e.g. , or ; or \t")]
    public string? Delimiter { get; set; }

    [PipeParameter(Label = "Encoding", Order = 7, Expressions = false, Placeholder = "utf-8",
        Description = "CSV, JSON, XML, Text: e.g. utf-8, windows-1252, iso-8859-1, utf-16")]
    public string? Encoding { get; set; }

    [PipeParameter(Label = "Sheet", Order = 8, Placeholder = "Sheet1", Description = "Excel: name of the sheet")]
    public string? Sheet { get; set; }

    [PipeParameter(Label = "Sheet per field", Order = 9, Expressions = false,
        Description = "Excel: one sheet per value of this field, e.g. region")]
    public string? SheetPerField { get; set; }

    [PipeParameter(Label = "Root element", Order = 10, Placeholder = "items",
        Description = "XML: name of the root element; for one file per item, the element of the item")]
    public string? RootElement { get; set; }

    [PipeParameter(Label = "Item element", Order = 11, Expressions = false, Placeholder = "item",
        Description = "XML, all items into one file: the element of each item")]
    public string? ItemElement { get; set; }

    [PipeParameter(Label = "Text field", Order = 12, Expressions = false, Placeholder = "text",
        Description = "Text: the field of the items to write, one item per line")]
    public string? TextField { get; set; }
}

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
