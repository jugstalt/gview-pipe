using System.Text.Json.Nodes;

namespace gView.Pipe.Abstraction.Nodes;

/// <summary>A value the editor offers for a parameter marked with <see cref="PipeOptionsAttribute"/>.</summary>
public sealed record PipeParameterOption(string Value, string? Label = null, string? Description = null);

/// <summary>
/// Implemented by nodes whose parameter values come from the server, depending on the other parameters
/// (e.g. the scripts found in a folder). The editor asks for them while the node is edited.
/// </summary>
public interface IPipeParameterOptions
{
    /// <param name="parameter">JSON name of the parameter (camelCase).</param>
    /// <param name="parameters">The node's current parameters, expressions not evaluated.</param>
    /// <param name="refresh">The user asked for fresh values (e.g. pull a repository again).</param>
    Task<IReadOnlyList<PipeParameterOption>> GetOptionsAsync(string parameter, JsonObject parameters, bool refresh, CancellationToken cancellationToken);
}

/// <summary>
/// The parameter's values come from <see cref="IPipeParameterOptions"/>: a string becomes a choice of one option,
/// a <c>List&lt;PipeSelection&gt;</c> an ordered list of options that can be switched on and off.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PipeOptionsAttribute : Attribute
{
    /// <summary>Show a refresh button that asks for the options with <c>refresh = true</c>.</summary>
    public bool Refreshable { get; set; }
}

/// <summary>An entry of an ordered selection list: the option's value and whether it is switched on.</summary>
public sealed class PipeSelection
{
    public string Value { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
