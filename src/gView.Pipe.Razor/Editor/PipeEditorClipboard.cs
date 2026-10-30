using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Workflows;
using Microsoft.JSInterop;
using System.Text.Json;

namespace gView.Pipe.Razor.Editor;

/// <summary>Copied nodes plus the connections between them, as they travel through the clipboard.</summary>
public sealed class PipeClipboardContent
{
    public const string Marker = "gview-pipe/nodes";

    /// <summary>Tells copied nodes apart from any other text in the clipboard.</summary>
    public string Format { get; set; } = Marker;

    public List<PipeWorkflowNode> Nodes { get; set; } = new();

    public List<PipeConnection> Connections { get; set; } = new();
}

/// <summary>
/// Copy and paste of nodes. Uses the browser clipboard (as JSON text), so nodes can move between workflows,
/// tabs and even people. Where the clipboard API is not available (it needs HTTPS or localhost, and reading
/// may be denied), a copy kept in memory for this session is used instead.
/// </summary>
public sealed class PipeEditorClipboard
{
    private readonly IJSRuntime _js;
    private string? _memory;

    public PipeEditorClipboard(IJSRuntime js)
    {
        _js = js;
    }

    public async Task CopyAsync(PipeClipboardContent content)
    {
        _memory = JsonSerializer.Serialize(content, PipeJson.Options);
        await TryWriteTextAsync(_memory);
    }

    /// <summary>Nodes from the browser clipboard, or from the session copy if the clipboard holds no nodes.</summary>
    public async Task<PipeClipboardContent?> PasteAsync()
    {
        try
        {
            var text = await _js.InvokeAsync<string?>("navigator.clipboard.readText");
            if (Parse(text) is { } content)
            {
                return content;
            }
        }
        catch (JSException) { }

        return Parse(_memory);
    }

    /// <summary>Puts plain text (e.g. an expression) into the browser clipboard. False if that is not possible.</summary>
    public async Task<bool> TryWriteTextAsync(string text)
    {
        try
        {
            await _js.InvokeVoidAsync("navigator.clipboard.writeText", text);
            return true;
        }
        catch (JSException)
        {
            return false;
        }
    }

    static private PipeClipboardContent? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains(PipeClipboardContent.Marker, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var content = JsonSerializer.Deserialize<PipeClipboardContent>(text, PipeJson.Options);
            return content?.Format == PipeClipboardContent.Marker && content.Nodes.Count > 0 ? content : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
