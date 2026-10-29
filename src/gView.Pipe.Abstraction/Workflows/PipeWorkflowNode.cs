using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace gView.Pipe.Abstraction.Workflows;

/// <summary>A node instance inside a workflow.</summary>
public sealed class PipeWorkflowNode
{
    /// <summary>Unique within the workflow; referenced by connections.</summary>
    public string Id { get; set; } = "";

    /// <summary>Unique within the workflow; referenced by expressions (<c>$node["Name"]</c>).</summary>
    public string Name { get; set; } = "";

    /// <summary>Node type id (<see cref="Nodes.PipeNodeAttribute.Id"/>).</summary>
    public Guid Type { get; set; }

    public int TypeVersion { get; set; } = 1;

    public JsonObject Parameters { get; set; } = new();

    public PipeNodeSettings Settings { get; set; } = new();

    public bool Disabled { get; set; }

    public PipePosition Position { get; set; } = new();

    public string? Notes { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; set; }
}

public sealed class PipeNodeSettings
{
    public int RetryCount { get; set; }

    public int RetryDelayMs { get; set; } = 1000;

    public PipeNodeErrorMode OnError { get; set; } = PipeNodeErrorMode.Stop;

    /// <summary>0 = no timeout.</summary>
    public int TimeoutSeconds { get; set; }

    /// <summary>Execute the node even if all inputs are empty.</summary>
    public bool AlwaysExecute { get; set; }
}

public enum PipeNodeErrorMode
{
    /// <summary>Stop the whole execution.</summary>
    Stop,

    /// <summary>Record the error and pass the input items on unchanged.</summary>
    Continue,

    /// <summary>Route the input items (with an <c>error</c> field) to the <c>error</c> output port.</summary>
    ErrorOutput
}

public sealed class PipePosition
{
    public double X { get; set; }
    public double Y { get; set; }
}
