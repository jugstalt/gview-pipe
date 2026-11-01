using System.Text.RegularExpressions;

namespace gView.Pipe.Abstraction.Variables;

/// <summary>
/// A named value on instance, group or workflow level, with one value per environment (see ADR 0013).
/// Expressions read it as <c>$vars.NAME</c>; script nodes get it as process environment.
/// </summary>
public sealed class PipeVariable
{
    /// <summary>Upper case, see <see cref="PipeVariableNames"/>.</summary>
    public string Name { get; set; } = "";

    /// <summary>Stored encrypted and masked in logs and script output.</summary>
    public bool Secret { get; set; }

    /// <summary>Value per environment name (<see cref="PipeEnvironments.Default"/> included).</summary>
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

static public partial class PipeVariableNames
{
    /// <summary>Prefix of the variables the script nodes set themselves.</summary>
    public const string ReservedPrefix = "PIPE_";

    static public string Normalize(string name) => name.Trim().ToUpperInvariant();

    /// <summary>Null when <paramref name="name"/> is valid, otherwise the reason.</summary>
    static public string? Validate(string name)
    {
        var normalized = Normalize(name);
        if (!ValidName().IsMatch(normalized))
        {
            return "Use letters, digits and '_', not starting with a digit";
        }
        return normalized.StartsWith(ReservedPrefix, StringComparison.Ordinal) ? $"The prefix {ReservedPrefix} is reserved" : null;
    }

    [GeneratedRegex("^[A-Z_][A-Z0-9_]*$")]
    static private partial Regex ValidName();
}

static public class PipeEnvironments
{
    /// <summary>Always exists; values for it apply when the active environment has none.</summary>
    public const string Default = "DEFAULT";

    static public string Normalize(string environment) => environment.Trim().ToUpperInvariant();
}

/// <summary>Where the effective environment of a workflow comes from.</summary>
public enum PipeEnvironmentSource
{
    Instance,
    Group,
    Workflow
}

/// <param name="SourceName">Name of the group for <see cref="PipeEnvironmentSource.Group"/>.</param>
public sealed record PipeEffectiveEnvironment(string Name, PipeEnvironmentSource Source, string? SourceName = null);

/// <summary>Variables of one execution: name → value for the active environment.</summary>
public sealed class PipeResolvedVariables
{
    static public PipeResolvedVariables Empty { get; } = new();

    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Names of the variables marked secret on any level.</summary>
    public IReadOnlySet<string> Secrets { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> SecretValues => Values.Where(v => Secrets.Contains(v.Key) && v.Value.Length > 0).Select(v => v.Value);
}
