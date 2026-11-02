using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Transform;

public enum DateTimeOperation
{
    Now,
    Format,
    Parse,
    Add,
    Subtract,
    Difference,
    ConvertTimeZone,
    StartOf
}

public enum DateTimeUnit
{
    Milliseconds,
    Seconds,
    Minutes,
    Hours,
    Days,
    Weeks,
    Months,
    Years
}

public sealed class DateTimeNodeParameters
{
    [PipeParameter(Label = "Operation", Order = 1, Expressions = false)]
    public DateTimeOperation Operation { get; set; } = DateTimeOperation.Format;

    [PipeParameter(Label = "Date", Order = 2, Placeholder = "{{ $json.createdAt }}",
        Description = "The date to work on: ISO 8601 like 2026-11-02T08:30:00+01:00, or text in the pattern (Parse), or Unix seconds or milliseconds. Not used by Now.")]
    public string? Value { get; set; }

    [PipeParameter(Label = "Until", Order = 3, Placeholder = "{{ $json.closedAt }}",
        Description = "Difference: the second date; empty means now. The result is positive when it is later than Date.")]
    public string? Until { get; set; }

    [PipeParameter(Label = "Pattern", Order = 4, Placeholder = "yyyy-MM-dd HH:mm",
        Description = ".NET date pattern. Format: the output (empty: ISO 8601). Parse: the input (empty: any common form).")]
    public string? Pattern { get; set; }

    [PipeParameter(Label = "Culture", Order = 5, Placeholder = "invariant",
        Description = "For month and day names, e.g. de-DE or en-US")]
    public string? Culture { get; set; }

    [PipeParameter(Label = "Amount", Order = 6, Expressions = false, Description = "Add, Subtract: how many units")]
    public double Amount { get; set; } = 1;

    [PipeParameter(Label = "Unit", Order = 7, Expressions = false, Description = "Add, Subtract, Difference, Start of")]
    public DateTimeUnit Unit { get; set; } = DateTimeUnit.Days;

    [PipeParameter(Label = "Time zone", Order = 8, Placeholder = "UTC",
        Description = "IANA or Windows id, e.g. Europe/Vienna. Convert time zone: the target. Now, Format, Start of: the zone to work in. Parse: the zone of text without offset.")]
    public string? TimeZone { get; set; }

    [Required, PipeParameter(Label = "Target field", Order = 9, Expressions = false, Description = "Field for the result, e.g. date or meta.due")]
    public string TargetField { get; set; } = "date";
}

[PipeNode(PipeBuiltInNodes.DateTime, "Date & Time",
    Category = "Transform",
    Description = "Gets the current time, formats, parses and calculates dates, and converts between time zones. Writes the result into a field of each item.")]
public sealed class DateTimeNode : PipeItemNodeBase<DateTimeNodeParameters>
{
    private readonly TimeProvider _time;

    public DateTimeNode(TimeProvider time)
    {
        _time = time;
    }

    protected override Task<PipeItem?> ProcessItemAsync(PipeItem item, DateTimeNodeParameters parameters, IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var zone = PipeDates.Zone(parameters.TimeZone);
        var culture = PipeDates.Culture(parameters.Culture);

        JsonNode? result = parameters.Operation switch
        {
            DateTimeOperation.Now => PipeDates.Iso(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), zone ?? TimeZoneInfo.Utc)),
            DateTimeOperation.Format => string.IsNullOrWhiteSpace(parameters.Pattern)
                ? PipeDates.Iso(InZone(Input(parameters, culture, null), zone))
                : InZone(Input(parameters, culture, null), zone).ToString(parameters.Pattern, culture),
            DateTimeOperation.Parse => PipeDates.Iso(Input(parameters, culture, zone, parameters.Pattern)),
            DateTimeOperation.Add => PipeDates.Iso(PipeDates.Add(Input(parameters, culture, null), parameters.Amount, parameters.Unit)),
            DateTimeOperation.Subtract => PipeDates.Iso(PipeDates.Add(Input(parameters, culture, null), -parameters.Amount, parameters.Unit)),
            DateTimeOperation.Difference => PipeDates.Difference(
                Input(parameters, culture, null),
                string.IsNullOrWhiteSpace(parameters.Until) ? _time.GetUtcNow() : PipeDates.Parse(parameters.Until, null, culture, null),
                parameters.Unit),
            DateTimeOperation.ConvertTimeZone => PipeDates.Iso(TimeZoneInfo.ConvertTime(Input(parameters, culture, null),
                zone ?? throw new PipeException("Convert time zone needs a time zone"))),
            DateTimeOperation.StartOf => PipeDates.Iso(PipeDates.StartOf(InZone(Input(parameters, culture, null), zone), parameters.Unit)),
            _ => throw new PipeException($"Unknown operation {parameters.Operation}")
        };

        var output = item.Clone();
        PipeJsonPath.Set(output.Json, parameters.TargetField, result);
        return Task.FromResult<PipeItem?>(output);
    }

    static private DateTimeOffset Input(DateTimeNodeParameters parameters, CultureInfo culture, TimeZoneInfo? zone, string? pattern = null)
        => PipeDates.Parse(parameters.Value, pattern, culture, zone);

    static private DateTimeOffset InZone(DateTimeOffset value, TimeZoneInfo? zone)
        => zone is null ? value : TimeZoneInfo.ConvertTime(value, zone);
}

static internal class PipeDates
{
    static public string Iso(DateTimeOffset value) => value.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);

    static public CultureInfo Culture(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Equals("invariant", StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.InvariantCulture;
        }
        try
        {
            return CultureInfo.GetCultureInfo(name.Trim());
        }
        catch (CultureNotFoundException)
        {
            throw new PipeException($"Unknown culture '{name}'; use e.g. de-DE or en-US");
        }
    }

    static public TimeZoneInfo? Zone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        if (id.Trim().Equals("UTC", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc;
        }
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            throw new PipeException($"Unknown time zone '{id}'; use an IANA id like Europe/Vienna or a Windows id");
        }
    }

    /// <summary>
    /// Text without an offset is in <paramref name="zone"/> (UTC when null). Digits only are Unix seconds, or milliseconds
    /// when they are too large for seconds.
    /// </summary>
    static public DateTimeOffset Parse(string? text, string? pattern, CultureInfo culture, TimeZoneInfo? zone)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            throw new PipeException("No date given");
        }

        if (string.IsNullOrWhiteSpace(pattern) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            return Math.Abs(unix) > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(unix) : DateTimeOffset.FromUnixTimeSeconds(unix);
        }

        DateTime local;
        var hasOffset = false;
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            if (!DateTime.TryParseExact(value, pattern, culture, DateTimeStyles.AllowWhiteSpaces, out local))
            {
                throw new PipeException($"'{value}' doesn't match the pattern '{pattern}'");
            }
            if (DateTimeOffset.TryParseExact(value, pattern, culture, DateTimeStyles.AllowWhiteSpaces, out var withOffset) && pattern.Contains('z'))
            {
                return withOffset;
            }
        }
        else if (DateTimeOffset.TryParse(value, culture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            // DateTimeOffset reports an offset either way; an explicit one is in the text
            hasOffset = value.EndsWith('Z') || value.EndsWith('z') || HasOffset(value);
            if (hasOffset || zone is null)
            {
                return parsed;
            }
            local = parsed.UtcDateTime;
        }
        else
        {
            throw new PipeException($"'{value}' isn't a date; set a pattern to parse it");
        }

        var target = zone ?? TimeZoneInfo.Utc;
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, target.GetUtcOffset(unspecified));
    }

    static private bool HasOffset(string value)
    {
        var time = value.IndexOf('T') is var t and > 0 ? value[t..] : value.Length > 10 ? value[10..] : "";
        return time.Contains('+') || time.LastIndexOf('-') > 0;
    }

    static public DateTimeOffset Add(DateTimeOffset value, double amount, DateTimeUnit unit) => unit switch
    {
        DateTimeUnit.Milliseconds => value.AddMilliseconds(amount),
        DateTimeUnit.Seconds => value.AddSeconds(amount),
        DateTimeUnit.Minutes => value.AddMinutes(amount),
        DateTimeUnit.Hours => value.AddHours(amount),
        DateTimeUnit.Days => value.AddDays(amount),
        DateTimeUnit.Weeks => value.AddDays(amount * 7),
        DateTimeUnit.Months => value.AddMonths(Whole(amount, unit)),
        DateTimeUnit.Years => value.AddYears(Whole(amount, unit)),
        _ => value
    };

    static private int Whole(double amount, DateTimeUnit unit)
        => amount == Math.Floor(amount) ? (int)amount : throw new PipeException($"{unit} can only be added in whole numbers");

    /// <summary>Months and years count whole calendar units; the others may have decimals.</summary>
    static public JsonNode Difference(DateTimeOffset from, DateTimeOffset until, DateTimeUnit unit)
    {
        var span = until - from;
        return unit switch
        {
            DateTimeUnit.Months => JsonValue.Create(WholeMonths(from, until)),
            DateTimeUnit.Years => JsonValue.Create(WholeMonths(from, until) / 12),
            _ => JsonValue.Create(unit switch
            {
                DateTimeUnit.Seconds => span.TotalSeconds,
                DateTimeUnit.Minutes => span.TotalMinutes,
                DateTimeUnit.Hours => span.TotalHours,
                DateTimeUnit.Days => span.TotalDays,
                DateTimeUnit.Weeks => span.TotalDays / 7,
                _ => span.TotalMilliseconds
            })
        };
    }

    static private int WholeMonths(DateTimeOffset from, DateTimeOffset until)
    {
        var sign = until >= from ? 1 : -1;
        var (a, b) = sign > 0 ? (from, until) : (until, from);
        var months = (b.Year - a.Year) * 12 + b.Month - a.Month;
        if (a.AddMonths(months) > b)
        {
            months--;
        }
        return sign * months;
    }

    static public DateTimeOffset StartOf(DateTimeOffset value, DateTimeUnit unit)
    {
        var start = unit switch
        {
            DateTimeUnit.Milliseconds => value.DateTime,
            DateTimeUnit.Seconds => new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second),
            DateTimeUnit.Minutes => new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0),
            DateTimeUnit.Hours => new DateTime(value.Year, value.Month, value.Day, value.Hour, 0, 0),
            DateTimeUnit.Days => value.Date,
            // weeks start on Monday (ISO 8601)
            DateTimeUnit.Weeks => value.Date.AddDays(-(((int)value.DayOfWeek + 6) % 7)),
            DateTimeUnit.Months => new DateTime(value.Year, value.Month, 1),
            DateTimeUnit.Years => new DateTime(value.Year, 1, 1),
            _ => value.DateTime
        };
        return new DateTimeOffset(start, value.Offset);
    }
}
