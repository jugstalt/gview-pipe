using gView.Pipe.Abstraction.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Abstraction.Workflows;

/// <summary>A short summary of what changed between two versions of a workflow, for the audit log (ADR 0022).</summary>
static public class PipeWorkflowChanges
{
    /// <returns>e.g. <c>nodes added: HTTP Request; changed: Set Greeting (parameters)</c>; <c>no changes</c> when equal.</returns>
    static public string Describe(PipeWorkflow before, PipeWorkflow after)
    {
        var parts = new List<string>();
        var old = before.Nodes.ToDictionary(n => n.Id);
        var now = after.Nodes.ToDictionary(n => n.Id);

        var added = after.Nodes.Where(n => !old.ContainsKey(n.Id)).Select(n => n.Name).ToList();
        var removed = before.Nodes.Where(n => !now.ContainsKey(n.Id)).Select(n => n.Name).ToList();
        var changed = after.Nodes.Where(n => old.ContainsKey(n.Id))
            .Select(n => (n.Name, What: NodeChanges(old[n.Id], n)))
            .Where(c => c.What.Count > 0)
            .Select(c => $"{c.Name} ({string.Join(", ", c.What)})")
            .ToList();

        if (added.Count > 0)
        {
            parts.Add($"nodes added: {string.Join(", ", added)}");
        }
        if (removed.Count > 0)
        {
            parts.Add($"removed: {string.Join(", ", removed)}");
        }
        if (changed.Count > 0)
        {
            parts.Add($"changed: {string.Join(", ", changed)}");
        }

        var connections = Connections(before).ToHashSet();
        var connectionsNow = Connections(after).ToHashSet();
        if (!connections.SetEquals(connectionsNow))
        {
            parts.Add($"connections +{connectionsNow.Except(connections).Count()} -{connections.Except(connectionsNow).Count()}");
        }

        if (before.Name != after.Name)
        {
            parts.Add($"renamed from '{before.Name}'");
        }
        if (before.GroupId != after.GroupId)
        {
            parts.Add($"group {before.GroupId ?? "none"} → {after.GroupId ?? "none"}");
        }
        if (before.Active != after.Active)
        {
            parts.Add(after.Active ? "activated" : "deactivated");
        }
        if (!SameJson(before.Settings, after.Settings) || before.Environment != after.Environment || before.Description != after.Description)
        {
            parts.Add("workflow settings");
        }
        if (!SameJson(before.Variables, after.Variables))
        {
            parts.Add("variables");
        }
        if (!SameJson(before.PinData, after.PinData))
        {
            parts.Add("pin data");
        }

        return parts.Count == 0 ? "no changes" : string.Join("; ", parts);
    }

    static private List<string> NodeChanges(PipeWorkflowNode before, PipeWorkflowNode after)
    {
        var what = new List<string>();
        if (before.Name != after.Name)
        {
            what.Add($"renamed from '{before.Name}'");
        }
        if (!JsonNode.DeepEquals(before.Parameters, after.Parameters))
        {
            what.Add("parameters");
        }
        if (!SameJson(before.Settings, after.Settings))
        {
            what.Add("settings");
        }
        if (before.Disabled != after.Disabled)
        {
            what.Add(after.Disabled ? "disabled" : "enabled");
        }
        return what;
    }

    static private IEnumerable<string> Connections(PipeWorkflow workflow)
        => workflow.Connections.Select(c => $"{c.SourceNodeId}:{c.SourcePort}>{c.TargetNodeId}:{c.TargetPort}");

    static private bool SameJson<T>(T a, T b) => JsonSerializer.Serialize(a, PipeJson.Options) == JsonSerializer.Serialize(b, PipeJson.Options);
}
