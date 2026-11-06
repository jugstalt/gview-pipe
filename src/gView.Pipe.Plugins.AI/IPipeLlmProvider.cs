using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.AI;

/// <summary>A part of the user content: text, a PDF document or an image.</summary>
public abstract record PipeLlmContent;

public sealed record PipeLlmText(string Text) : PipeLlmContent;

public sealed record PipeLlmDocument(byte[] Pdf, string? Title) : PipeLlmContent;

public sealed record PipeLlmImage(byte[] Data, string MediaType) : PipeLlmContent;

/// <param name="Model">The model to use; providers whose credential decides the model (Azure OpenAI) ignore it.</param>
/// <param name="Effort">low, medium or high; ignored by models that don't think.</param>
/// <param name="JsonSchema">Constrains the answer to a JSON object of this schema (structured outputs); null for text.</param>
/// <param name="UseFallbacks">Ask the service to try another model when this one declines the request.</param>
public sealed record PipeLlmRequest(
    string Model,
    string System,
    IReadOnlyList<PipeLlmContent> Content,
    int MaxTokens,
    string Effort,
    JsonObject? JsonSchema,
    bool UseFallbacks);

/// <param name="Text">The text of the answer; the JSON when a schema was given.</param>
/// <param name="StopReason">end_turn, max_tokens or refusal; providers map their own reasons to these.</param>
/// <param name="Model">The model that answered, which may be a fallback.</param>
public sealed record PipeLlmResponse(string Text, string StopReason, string Model, long InputTokens, long OutputTokens, string? RefusalCategory = null);

/// <summary>
/// One request to a language model service (ADR 0041). The AI node talks only to this, so it is tested without a service.
/// The credential of the node picks the provider (ADR 0042).
/// </summary>
public interface IPipeLlmProvider
{
    /// <summary>The id of the credential type this provider serves.</summary>
    string CredentialTypeId { get; }

    /// <param name="credential">The fields of the credential, secrets already resolved.</param>
    /// <exception cref="gView.Pipe.Abstraction.PipeException">The service refused the call (key, limits, request, content type); the message has no secret.</exception>
    Task<PipeLlmResponse> CompleteAsync(JsonObject credential, PipeLlmRequest request, CancellationToken cancellationToken);
}
