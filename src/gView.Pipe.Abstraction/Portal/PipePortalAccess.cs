using gView.Pipe.Abstraction.Workflows;

namespace gView.Pipe.Abstraction.Portal;

/// <summary>Which portal users may see a workflow (ADR 0030).</summary>
static public class PipePortalAccess
{
    /// <summary>
    /// True when a user with <paramref name="userRoles"/> may see and run the workflow in the portal: the roles of the
    /// workflow decide; without them the roles of its group; without both everybody may. Roles are compared ignoring case.
    /// </summary>
    static public bool Allows(IReadOnlyCollection<string> userRoles, PipeWorkflow workflow, PipeWorkflowGroup? group)
    {
        var required = workflow.Settings.Portal?.Roles is { Count: > 0 } own ? own
            : group?.PortalRoles is { Count: > 0 } inherited ? inherited
            : null;
        return required is null || required.Any(role => userRoles.Contains(role, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Roles typed as a list, e.g. <c>hr, finance</c>: trimmed, without empty entries and doubles. Null when none.</summary>
    static public List<string>? Parse(string? text)
    {
        var roles = (text ?? "")
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return roles.Count == 0 ? null : roles;
    }
}
