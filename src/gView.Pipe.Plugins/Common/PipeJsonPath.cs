using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Common;

/// <summary>Dotted paths (<c>address.city</c>) into JSON objects.</summary>
static public class PipeJsonPath
{
    /// <summary>The value at the path, or null if a part of it is missing.</summary>
    static public JsonNode? Get(JsonObject root, string path)
    {
        JsonNode? current = root;
        foreach (var part in path.Split('.'))
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(part, out current))
            {
                return null;
            }
        }
        return current;
    }

    static public void Set(JsonObject root, string path, JsonNode? value)
    {
        var parts = path.Split('.');
        var current = root;

        foreach (var part in parts[..^1])
        {
            if (current[part] is not JsonObject child)
            {
                current[part] = child = new JsonObject();
            }
            current = child;
        }

        current[parts[^1]] = value;
    }

    static public bool TryRemove(JsonObject root, string path, out JsonNode? value)
    {
        value = null;
        var parts = path.Split('.');
        var current = root;

        foreach (var part in parts[..^1])
        {
            if (current[part] is not JsonObject child)
            {
                return false;
            }
            current = child;
        }

        if (!current.TryGetPropertyValue(parts[^1], out value))
        {
            return false;
        }

        current.Remove(parts[^1]);
        return true;
    }
}
