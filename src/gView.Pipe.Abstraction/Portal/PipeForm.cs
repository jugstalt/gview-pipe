using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace gView.Pipe.Abstraction.Portal;

public enum PipeFormFieldType
{
    Text,
    MultilineText,
    Number,
    Boolean,
    Date,
    Choice,
    Password
}

/// <summary>One input of a form (ADR 0027).</summary>
public sealed class PipeFormField
{
    /// <summary>Name of the property in the item the workflow receives.</summary>
    public string Name { get; set; } = "";

    public string Label { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>Hint shown in the empty input.</summary>
    public string? Placeholder { get; set; }

    public PipeFormFieldType Type { get; set; }

    /// <summary>
    /// Regular expression a text must match as a whole (text, multiline text, password). The portal checks it while
    /// typing, so keep it to what .NET and JavaScript both understand; the backend checks it again.
    /// </summary>
    public string? Pattern { get; set; }

    /// <summary>Shown when the text doesn't match <see cref="Pattern"/>.</summary>
    public string? PatternMessage { get; set; }

    public bool Required { get; set; }

    public JsonNode? Default { get; set; }

    /// <summary>Allowed values of a <see cref="PipeFormFieldType.Choice"/>.</summary>
    public List<string> Options { get; set; } = [];
}

/// <summary>The form of a workflow: what a portal user is asked for. Read from its Form Trigger node.</summary>
public sealed class PipeForm
{
    public List<PipeFormField> Fields { get; set; } = [];

    /// <summary>Reads the fields from the JSON array of a Form Trigger; entries that aren't valid are skipped.</summary>
    static public PipeForm FromJson(JsonArray? fields)
    {
        var form = new PipeForm();
        foreach (var node in fields ?? [])
        {
            if (node is not JsonObject o || (string?)o["name"] is not { Length: > 0 } name || form.Fields.Any(f => f.Name == name))
            {
                continue;
            }
            var type = Enum.TryParse<PipeFormFieldType>((string?)o["type"], ignoreCase: true, out var t) ? t : PipeFormFieldType.Text;
            form.Fields.Add(new PipeFormField
            {
                Name = name,
                Label = (string?)o["label"] is { Length: > 0 } label ? label : name,
                Description = (string?)o["description"],
                Placeholder = (string?)o["placeholder"],
                Pattern = (string?)o["pattern"] is { Length: > 0 } pattern ? pattern : null,
                PatternMessage = (string?)o["patternMessage"],
                Type = type,
                Required = (bool?)o["required"] ?? false,
                Default = o["default"]?.DeepClone(),
                Options = o["options"] is JsonArray options ? options.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToList() : []
            });
        }
        return form;
    }

    /// <summary>True when <paramref name="text"/> matches the pattern as a whole. A pattern that isn't valid matches nothing.</summary>
    static public bool Matches(string pattern, string text)
    {
        try
        {
            return Regex.IsMatch(text, $@"\A(?:{pattern})\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Checks <paramref name="values"/> against the form. Returns the item to run with (only known fields, typed,
    /// defaults filled in) and the problems per field; the caller must not run when there are problems.
    /// </summary>
    public (JsonObject Item, Dictionary<string, string> Problems) Validate(JsonObject? values)
    {
        var item = new JsonObject();
        var problems = new Dictionary<string, string>();
        foreach (var field in Fields)
        {
            var value = values?[field.Name] ?? field.Default?.DeepClone();
            var text = value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value?.ToString();
            if (field.Type == PipeFormFieldType.Boolean)
            {
                if (value is null || text is { Length: 0 })
                {
                    item[field.Name] = false;
                }
                else if (!bool.TryParse(text, out var flag))
                {
                    problems[field.Name] = "Not true or false";
                }
                else
                {
                    item[field.Name] = flag;
                }
                continue;
            }
            if (string.IsNullOrEmpty(text))
            {
                if (field.Required)
                {
                    problems[field.Name] = "Required";
                }
                continue;
            }

            switch (field.Type)
            {
                case PipeFormFieldType.Number:
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        problems[field.Name] = "Not a number";
                    }
                    else
                    {
                        item[field.Name] = number;
                    }
                    break;
                case PipeFormFieldType.Date:
                    if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                    {
                        problems[field.Name] = "Not a date";
                    }
                    else
                    {
                        // a date without time stays a date
                        item[field.Name] = text.Length <= 10 ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : date.ToString("O", CultureInfo.InvariantCulture);
                    }
                    break;
                case PipeFormFieldType.Choice:
                    if (!field.Options.Contains(text))
                    {
                        problems[field.Name] = "Not one of the choices";
                    }
                    else
                    {
                        item[field.Name] = text;
                    }
                    break;
                default:
                    if (field.Pattern is not null && !Matches(field.Pattern, text))
                    {
                        problems[field.Name] = string.IsNullOrWhiteSpace(field.PatternMessage) ? "Doesn't have the expected form" : field.PatternMessage;
                    }
                    else
                    {
                        item[field.Name] = text;
                    }
                    break;
            }
        }
        return (item, problems);
    }
}

/// <summary>What a portal user sees at the end of a run, produced by a Result node (ADR 0027).</summary>
public sealed class PipeResultView
{
    /// <summary><c>text</c>, <c>markdown</c> or <c>table</c>.</summary>
    public string Kind { get; set; } = "text";

    public string? Title { get; set; }

    public string? Text { get; set; }

    public List<string>? Columns { get; set; }

    public List<List<string?>>? Rows { get; set; }

    /// <summary>Shown instead of the error of the run when it fails.</summary>
    public string? ErrorMessage { get; set; }
}
