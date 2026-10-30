using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Common;

/// <summary>CSV as in RFC 4180: quoted fields may contain delimiters, quotes ("") and line breaks.</summary>
static public class PipeCsv
{
    /// <summary>The delimiter of a setting: its first character; <c>\t</c> or <c>tab</c> for a tab, empty for a comma.</summary>
    static public char Delimiter(string? setting)
        => string.IsNullOrEmpty(setting) ? ','
            : setting is @"\t" or "tab" or "\t" ? '\t'
            : setting[0];

    /// <summary>One object per data row; the first row holds the column names. All values are strings.</summary>
    static public List<JsonObject> Parse(string text, char delimiter = ',')
    {
        var rows = ReadRows(text, delimiter);
        if (rows.Count == 0)
        {
            return [];
        }

        var header = rows[0].Select((name, i) => string.IsNullOrWhiteSpace(name) ? $"column{i + 1}" : name.Trim()).ToList();
        var result = new List<JsonObject>();
        foreach (var row in rows.Skip(1))
        {
            // a trailing empty line is not a row
            if (row.Count == 1 && row[0].Length == 0)
            {
                continue;
            }

            var json = new JsonObject();
            for (var i = 0; i < Math.Max(header.Count, row.Count); i++)
            {
                json[i < header.Count ? header[i] : $"column{i + 1}"] = i < row.Count ? row[i] : "";
            }
            result.Add(json);
        }
        return result;
    }

    /// <summary>Header (union of the top-level fields, in order of appearance) plus one line per item.</summary>
    static public string Write(IEnumerable<JsonObject> items, char delimiter = ',', bool includeHeader = true)
    {
        var list = items.ToList();
        var columns = list.SelectMany(i => i.Select(p => p.Key)).Distinct().ToList();
        var builder = new StringBuilder();

        if (includeHeader)
        {
            builder.Append(string.Join(delimiter, columns.Select(c => Quote(c, delimiter)))).Append("\r\n");
        }
        foreach (var item in list)
        {
            builder.Append(string.Join(delimiter, columns.Select(c => Quote(ToText(item[c]), delimiter)))).Append("\r\n");
        }
        return builder.ToString();
    }

    static private string ToText(JsonNode? value) => value switch
    {
        null => "",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        // numbers and booleans as written in JSON (invariant, no precision loss)
        JsonValue v => v.ToJsonString(),
        // nested objects and arrays stay JSON inside the cell
        _ => value.ToJsonString()
    };

    static private string Quote(string value, char delimiter)
        => value.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    static private List<List<string>> ReadRows(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }
}
