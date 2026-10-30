using gView.Pipe.Abstraction.Data;
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

    /// <summary>Active workflows have their triggers (cron, webhook, …) armed.</summary>
    public bool Active { get; set; }

    public PipeWorkflowSettings Settings { get; set; } = new();

    public List<PipeWorkflowNode> Nodes { get; set; } = new();

    public List<PipeConnection> Connections { get; set; } = new();

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
    /// <summary>Runs beyond this limit are skipped (and logged).</summary>
    public int MaxConcurrentRuns { get; set; } = 1;

    /// <summary>When false, executions are stored without item data (status only).</summary>
    public bool SaveExecutionData { get; set; } = true;

    /// <summary>
    /// Workflow (with an <see cref="Triggers.IPipeErrorTrigger"/> node) started when a triggered execution of this
    /// workflow fails. Manual runs don't start it.
    /// </summary>
    public string? ErrorWorkflowId { get; set; }
}
