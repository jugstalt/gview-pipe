using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace gView.Pipe.Abstraction.Triggers;

/// <param name="Path">Path below the webhook prefix (e.g. <c>orders/new</c>); unique across all active workflows.</param>
/// <param name="Method">HTTP method in upper case, or <c>*</c> for any.</param>
public sealed record PipeWebhookRegistration(
    string Path,
    string Method,
    string WorkflowId,
    string WorkflowName,
    string NodeName,
    Func<PipeWebhookRequest, CancellationToken, Task<PipeWebhookResponse>> Handler);

/// <summary>An incoming webhook call, already read and parsed by the HTTP host.</summary>
public sealed class PipeWebhookRequest
{
    public string Method { get; init; } = "GET";

    /// <summary>Normalized path (see <see cref="PipeWebhookPaths.Normalize"/>).</summary>
    public string Path { get; init; } = "";

    public Dictionary<string, string> Query { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Header names in lower case; multiple values joined with ", ".</summary>
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string? ContentType { get; init; }

    /// <summary>JSON body, form fields as object, or text.</summary>
    public JsonNode? Body { get; init; }

    public List<PipeWebhookFile> Files { get; init; } = new();
}

public sealed record PipeWebhookFile(string FieldName, string FileName, string ContentType, byte[] Content);

public sealed class PipeWebhookResponse
{
    public int StatusCode { get; init; } = 200;

    public JsonNode? Body { get; init; }

    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    static public PipeWebhookResponse Json(int statusCode, JsonNode? body) => new() { StatusCode = statusCode, Body = body };

    static public PipeWebhookResponse Error(int statusCode, string message, string? executionId = null)
    {
        var body = new JsonObject { ["error"] = message };
        if (executionId is not null)
        {
            body["executionId"] = executionId;
        }
        return Json(statusCode, body);
    }
}

/// <summary>Dispatches webhook calls to the registered triggers. Used by the HTTP host.</summary>
public interface IPipeWebhookRouter
{
    /// <summary>404 for unknown paths, 405 for a wrong method, otherwise the trigger's response.</summary>
    Task<PipeWebhookResponse> HandleAsync(PipeWebhookRequest request, CancellationToken cancellationToken);
}

static public partial class PipeWebhookPaths
{
    /// <summary>Trims slashes and lower-cases; throws for characters other than letters, digits, '-', '_', '/'.</summary>
    static public string Normalize(string path)
    {
        var normalized = path.Trim().Trim('/').ToLowerInvariant();
        return normalized.Length > 0 && ValidPath().IsMatch(normalized)
            ? normalized
            : throw new PipeException($"Invalid webhook path '{path}': use letters, digits, '-', '_' and '/'");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_\\-/]{0,199}$")]
    static private partial Regex ValidPath();
}
