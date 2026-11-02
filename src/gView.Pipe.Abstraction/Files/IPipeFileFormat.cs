using System.Text.Json.Nodes;

namespace gView.Pipe.Abstraction.Files;

/// <summary>
/// A file format that converts between a byte stream and JSON objects (see ADR 0018). Registered as a service
/// (<c>services.AddSingleton&lt;IPipeFileFormat, …&gt;()</c>, plugins in their startup); used by Extract from File and
/// Convert to File.
/// </summary>
public interface IPipeFileFormat
{
    /// <summary>Unique key, lower case, e.g. <c>csv</c>; stored in workflows.</summary>
    string Name { get; }

    /// <summary>Shown in the editor, e.g. <c>CSV</c>.</summary>
    string Label { get; }

    /// <summary>With the dot, e.g. <c>.csv</c>.</summary>
    string Extension { get; }

    string MimeType { get; }

    /// <summary>Rows of flat objects (CSV, Excel): all items always go into one file.</summary>
    bool IsTable { get; }

    /// <summary>The content as objects: rows of a table, elements of an array, or one object.</summary>
    Task<IReadOnlyList<JsonObject>> ReadAsync(Stream content, PipeFileFormatOptions options, CancellationToken cancellationToken);

    /// <summary>Writes the objects as one file.</summary>
    Task WriteAsync(Stream target, IReadOnlyList<JsonObject> items, PipeFileFormatOptions options, CancellationToken cancellationToken);
}

/// <summary>Settings a format may use; each format ignores what doesn't apply to it.</summary>
public sealed record PipeFileFormatOptions
{
    /// <summary>CSV: the delimiter; null or empty detects it when reading and uses a comma when writing.</summary>
    public string? Delimiter { get; init; }

    /// <summary>Text formats: e.g. <c>utf-8</c>, <c>windows-1252</c>; null reads UTF-8 (or what a BOM says) and writes UTF-8.</summary>
    public string? Encoding { get; init; }

    /// <summary>Tables: the first row holds the column names.</summary>
    public bool Header { get; init; } = true;

    /// <summary>Excel: sheet to read, or name of the sheet to write.</summary>
    public string? Sheet { get; init; }

    /// <summary>Excel: write one sheet per value of this field.</summary>
    public string? SheetPerField { get; init; }

    /// <summary>XML: name of the root element when writing.</summary>
    public string? RootElement { get; init; }

    /// <summary>XML: when reading, every element of this name becomes an object; when writing, the element of each item.</summary>
    public string? ItemElement { get; init; }

    /// <summary>XML: elements read as arrays even when they occur once.</summary>
    public IReadOnlyCollection<string>? AlwaysArray { get; init; }

    /// <summary>Text: the field of the items to write.</summary>
    public string? TextField { get; init; }

    /// <summary>JSON, XML: the file holds one item as itself (an object, the root element), not a list of one.</summary>
    public bool SingleItem { get; init; }

    /// <summary>JSON, XML: indented output.</summary>
    public bool Indent { get; init; } = true;
}
