using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Variables;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace gView.Pipe.Abstraction.Workflows;

/// <summary>A workflow definition as it is stored (one JSON document).</summary>
public sealed class PipeWorkflow
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>
    /// Group the workflow is in (<see cref="PipeWorkflowGroup.Id"/>), or null. An id of a group that doesn't exist
    /// (e.g. a file copied from another installation) is kept, and the workflow is shown without group.
    /// </summary>
    public string? GroupId { get; set; }

    /// <summary>Environment the workflow runs in, or null to inherit it from the group and the instance.</summary>
    public string? Environment { get; set; }

    /// <summary>Variables of the workflow level (see <see cref="PipeVariable"/>).</summary>
    public List<PipeVariable>? Variables { get; set; }

    /// <summary>Active workflows have their triggers (cron, webhook, …) armed.</summary>
    public bool Active { get; set; }

    public PipeWorkflowSettings Settings { get; set; } = new();

    public List<PipeWorkflowNode> Nodes { get; set; } = new();

    public List<PipeConnection> Connections { get; set; } = new();

    /// <summary>Notes on the canvas. Only the editor shows them; a missing list means none (ADR 0024).</summary>
    public List<PipeStickyNote>? StickyNotes { get; set; }

    /// <summary>
    /// Pin data: fixed output items per node id and output port. In manual runs a pinned node is not executed;
    /// it returns these items instead. Trigger runs ignore pin data.
    /// </summary>
    public Dictionary<string, Dictionary<string, List<PipeItem>>>? PinData { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Unknown properties are kept so newer files survive a round trip through older versions.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

public sealed class PipeWorkflowSettings
{
    /// <summary>Runs beyond this limit wait (see <see cref="MaxQueuedRuns"/>) or are skipped (and logged).</summary>
    public int MaxConcurrentRuns { get; set; } = 1;

    /// <summary>Triggered runs that may wait for a free slot; 0 = skip them (see ADR 0016).</summary>
    public int MaxQueuedRuns { get; set; }

    /// <summary>When false, executions are stored without item data (status only).</summary>
    public bool SaveExecutionData { get; set; } = true;

    /// <summary>Executions kept for this workflow; null = the instance setting.</summary>
    public int? ExecutionRetention { get; set; }

    /// <summary>Days executions are kept for this workflow (0 = no limit); null = the instance setting.</summary>
    public int? ExecutionRetentionDays { get; set; }

    /// <summary>
    /// Workflow (with an <see cref="Triggers.IPipeErrorTrigger"/> node) started when a triggered execution of this
    /// workflow fails. Manual runs don't start it. Null inherits the group's or the instance's;
    /// <see cref="PipeErrorWorkflows.None"/> switches it off (ADR 0017).
    /// </summary>
    public string? ErrorWorkflowId { get; set; }

    /// <summary>
    /// Environments a webhook caller may choose with the header <c>X-Pipe-Environment</c>. Empty: the caller can't.
    /// </summary>
    public List<string>? WebhookEnvironments { get; set; }

    /// <summary>How the workflow appears in the portal; null: not published (ADR 0027).</summary>
    public PipeWorkflowPortalSettings? Portal { get; set; }
}

public enum PipePortalRunMode
{
    /// <summary>The user waits (up to the timeout); then the run continues in the background.</summary>
    Wait,

    /// <summary>The run starts and the user comes back later.</summary>
    Background
}

/// <summary>Portal settings of a workflow (ADR 0027).</summary>
public sealed class PipeWorkflowPortalSettings
{
    /// <summary>Only published workflows are visible to portal users; the group is the category.</summary>
    public bool Published { get; set; }

    public PipePortalRunMode DefaultMode { get; set; } = PipePortalRunMode.Wait;

    /// <summary>Whether the user may choose the other mode when starting.</summary>
    public bool AllowModeChoice { get; set; } = true;

    /// <summary>Seconds a user waits before the run goes on in the background.</summary>
    public int WaitTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Only users with one of these roles see and run the workflow in the portal; empty: the roles of the group, and
    /// without those everybody (ADR 0030).
    /// </summary>
    public List<string>? Roles { get; set; }

    /// <summary>When false, users can't stop their runs (e.g. imports that must not end halfway).</summary>
    public bool AllowCancel { get; set; } = true;

    /// <summary>At most one run at a time, whoever started it.</summary>
    public bool SingleRun { get; set; }
}
