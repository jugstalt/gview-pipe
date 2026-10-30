namespace gView.Pipe.Abstraction.Workflows;

/// <summary>
/// A folder for workflows: a workflow is in at most one group (<see cref="PipeWorkflow.GroupId"/>); groups don't nest.
/// The id stays fixed, so groups can later serve as the unit for permissions.
/// </summary>
public sealed class PipeWorkflowGroup
{
    public string Id { get; set; } = "";

    /// <summary>Unique across groups, ignoring case.</summary>
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>One of <see cref="PipeWorkflowGroupColors.All"/>.</summary>
    public string Color { get; set; } = PipeWorkflowGroupColors.Blue;

    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary>Named colors of groups; the UI maps them to values that work in light and dark mode.</summary>
static public class PipeWorkflowGroupColors
{
    public const string Blue = "blue";
    public const string Teal = "teal";
    public const string Green = "green";
    public const string Amber = "amber";
    public const string Orange = "orange";
    public const string Red = "red";
    public const string Purple = "purple";
    public const string Gray = "gray";

    static public IReadOnlyList<string> All { get; } = [Blue, Teal, Green, Amber, Orange, Red, Purple, Gray];
}
