using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Files;
using gView.Pipe.Abstraction.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MiniExcelLibs;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Office;

public sealed class PipeOfficePluginStartup : IPipePluginStartup
{
    public void ConfigureServices(IServiceCollection services)
        => services.TryAddEnumerable(ServiceDescriptor.Singleton<IPipeFileFormat, PipeExcelFileFormat>());
}

/// <summary>Excel workbooks (.xlsx) as tables, with MiniExcel (see ADR 0018).</summary>
public sealed class PipeExcelFileFormat : IPipeFileFormat
{
    public string Name => "xlsx";
    public string Label => "Excel";
    public string Extension => ".xlsx";
    public string MimeType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public bool IsTable => true;

    public async Task<IReadOnlyList<JsonObject>> ReadAsync(Stream content, PipeFileFormatOptions options, CancellationToken cancellationToken)
    {
        // MiniExcel needs a seekable stream
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var sheets = MiniExcel.GetSheetNames(buffer);
        buffer.Position = 0;
        var sheet = string.IsNullOrWhiteSpace(options.Sheet) ? sheets.FirstOrDefault() : options.Sheet.Trim();
        if (sheet is null)
        {
            return [];
        }
        if (!sheets.Contains(sheet))
        {
            throw new PipeException($"The workbook has no sheet '{sheet}'; it has: {string.Join(", ", sheets)}");
        }

        var rows = new List<JsonObject>();
        foreach (IDictionary<string, object?> row in MiniExcel.Query(buffer, useHeaderRow: options.Header, sheetName: sheet))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.Values.All(v => v is null || v is string { Length: 0 }))
            {
                continue;
            }

            var json = new JsonObject();
            var index = 0;
            foreach (var (key, value) in row)
            {
                index++;
                // without a header MiniExcel names the columns A, B, …; like CSV they become column1, column2, …
                json[options.Header && !string.IsNullOrWhiteSpace(key) ? key.Trim() : $"column{index}"] = ToJson(value);
            }
            rows.Add(json);
        }
        return rows;
    }

    public async Task WriteAsync(Stream target, IReadOnlyList<JsonObject> items, PipeFileFormatOptions options, CancellationToken cancellationToken)
    {
        object value;
        if (!string.IsNullOrWhiteSpace(options.SheetPerField))
        {
            var field = options.SheetPerField.Trim();
            var sheets = new Dictionary<string, object>();
            foreach (var group in items.GroupBy(i => SheetName(i[field])))
            {
                sheets[group.Key] = Rows(group.ToList());
            }
            value = sheets.Count > 0 ? sheets : new Dictionary<string, object> { ["Sheet1"] = Rows([]) };
        }
        else
        {
            value = new Dictionary<string, object> { [string.IsNullOrWhiteSpace(options.Sheet) ? "Sheet1" : SheetName(options.Sheet)] = Rows(items) };
        }

        // MiniExcel writes into a seekable stream
        using var buffer = new MemoryStream();
        await MiniExcel.SaveAsAsync(buffer, value, printHeader: options.Header, cancellationToken: cancellationToken);
        buffer.Position = 0;
        await buffer.CopyToAsync(target, cancellationToken);
    }

    /// <summary>Every row has every column (the union of the fields in order of appearance), so no cell shifts.</summary>
    static private List<Dictionary<string, object?>> Rows(IReadOnlyList<JsonObject> items)
    {
        var columns = items.SelectMany(i => i.Select(p => p.Key)).Distinct().ToList();
        return items.Select(item => columns.ToDictionary(c => c, c => ToCell(item[c]))).ToList();
    }

    static private object? ToCell(JsonNode? value) => value switch
    {
        null => null,
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        JsonValue v when v.GetValueKind() == JsonValueKind.Number => v.GetValue<double>(),
        JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False => v.GetValue<bool>(),
        // nested objects and arrays stay JSON inside the cell, as in CSV
        _ => value.ToJsonString()
    };

    static private JsonNode? ToJson(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag,
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        double number when number == Math.Floor(number) && Math.Abs(number) < 1e15 => (long)number,
        double number => number,
        decimal number => number,
        int number => number,
        long number => number,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    /// <summary>Excel allows 31 characters and no : \ / ? * [ ] in sheet names.</summary>
    static private string SheetName(JsonNode? value)
        => SheetName(value is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : value?.ToJsonString() ?? "");

    static private string SheetName(string name)
    {
        var clean = new string(name.Select(c => ":\\/?*[]".Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? "(empty)" : clean.Length > 31 ? clean[..31] : clean;
    }
}
