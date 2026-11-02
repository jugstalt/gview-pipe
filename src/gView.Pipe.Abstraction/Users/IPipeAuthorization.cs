namespace gView.Pipe.Abstraction.Users;

/// <summary>Roles of the UI, each with the rights of the ones before (ADR 0021).</summary>
public enum PipeRole
{
    /// <summary>Sees workflows, executions and groups.</summary>
    Viewer = 1,

    /// <summary>Runs, stops and replays; activates and deactivates workflows.</summary>
    Operator = 2,

    /// <summary>Creates, changes and deletes workflows and groups.</summary>
    Editor = 3,

    /// <summary>Manages credentials, instance variables and API keys.</summary>
    Admin = 4
}

public enum PipePermission
{
    View,
    Run,
    Edit,
    Administer
}

/// <summary>The roles of a user: one for the instance, and others for single groups.</summary>
public sealed record PipeUserRoles(PipeRole Global, IReadOnlyDictionary<string, PipeRole> Groups)
{
    /// <summary>Who the user is, for the audit log (ADR 0022): the login name, or <c>anonymous (address)</c> without login.</summary>
    public string? UserName { get; init; }

    static public PipeUserRoles FullAccess { get; } = new(PipeRole.Admin, new Dictionary<string, PipeRole>());

    /// <summary>Until the roles are known: look only.</summary>
    static public PipeUserRoles ViewOnly { get; } = new(PipeRole.Viewer, new Dictionary<string, PipeRole>());

    /// <summary>The role for the workflows of a group (or of none); the group's when it has one.</summary>
    public PipeRole Role(string? groupId = null)
        => groupId is not null && Groups.TryGetValue(groupId, out var role) ? role : Global;

    /// <summary>
    /// Credentials, instance variables and API keys (<see cref="PipePermission.Administer"/>) only look at the instance role.
    /// </summary>
    public bool Can(PipePermission permission, string? groupId = null)
        => (permission == PipePermission.Administer ? Global : Role(groupId)) >= Needed(permission);

    static public PipeRole Needed(PipePermission permission) => permission switch
    {
        PipePermission.Run => PipeRole.Operator,
        PipePermission.Edit => PipeRole.Editor,
        PipePermission.Administer => PipeRole.Admin,
        _ => PipeRole.Viewer
    };

    /// <summary>A short text for a disabled button.</summary>
    static public string NotAllowed(PipePermission permission) => $"Needs the role {Needed(permission).ToString().ToLowerInvariant()}";
}

/// <summary>
/// The roles of the current user (scoped: per Blazor circuit or request). Components check them before they save,
/// delete or run (ADR 0021).
/// </summary>
public interface IPipeAuthorization
{
    Task<PipeUserRoles> GetRolesAsync();
}
