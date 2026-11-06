using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.AI;

/// <summary>The Anthropic SDK side of the AI node (ADR 0041): a thin adapter that is not tested against the service.</summary>
internal sealed class ClaudeLlmProvider : IPipeLlmProvider
{
    public string CredentialTypeId => AnthropicCredential.TypeId;

    public async Task<PipeLlmResponse> CompleteAsync(JsonObject credentialData, PipeLlmRequest request, CancellationToken cancellationToken)
    {
        var credential = credentialData.Deserialize<AnthropicCredential>(PipeJson.Options) ?? new AnthropicCredential();
        if (string.IsNullOrWhiteSpace(credential.ApiKey))
        {
            throw new PipeException("The credential has no API key");
        }

        var client = new AnthropicClient { ApiKey = credential.ApiKey.Trim() };

        var content = new List<BetaContentBlockParam>();
        foreach (var part in request.Content)
        {
            switch (part)
            {
                case PipeLlmDocument document:
                    content.Add(new BetaRequestDocumentBlock
                    {
                        Source = new BetaBase64PdfSource { Data = Convert.ToBase64String(document.Pdf) },
                        Title = document.Title
                    });
                    break;
                case PipeLlmImage image:
                    content.Add(new BetaImageBlockParam
                    {
                        Source = new BetaBase64ImageSource { Data = Convert.ToBase64String(image.Data), MediaType = image.MediaType }
                    });
                    break;
                case PipeLlmText plain:
                    content.Add(new BetaTextBlockParam { Text = plain.Text });
                    break;
            }
        }

        var outputConfig = new BetaOutputConfig
        {
            Effort = request.Effort switch
            {
                "high" => Effort.High,
                "medium" => Effort.Medium,
                _ => Effort.Low
            },
            // structured outputs: the answer is a JSON object of this schema
            Format = request.JsonSchema is { } schema
                ? new BetaJsonOutputFormat { Schema = schema.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)) }
                : null
        };

        var parameters = new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = request.MaxTokens,
            System = request.System,
            Messages = [new BetaUserMessageParam { Content = content }],
            OutputConfig = outputConfig,
            // a declined request is tried on the model Anthropic recommends for that case
            Betas = request.UseFallbacks ? ["server-side-fallback-2026-07-01"] : null,
            Fallbacks = request.UseFallbacks ? new BetaFallbacksParam(new Default()) : null
        };

        BetaMessage response;
        try
        {
            response = await client.Beta.Messages.Create(parameters, cancellationToken);
        }
        catch (AnthropicApiException ex)
        {
            // status and kind of the error; the request and the key are not part of it
            throw new PipeException($"The Anthropic API answered {(int)ex.StatusCode}: {Shorten(ex.Message)}");
        }

        var text = new StringBuilder();
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var part))
            {
                text.Append(part.Text);
            }
        }

        return new PipeLlmResponse(
            text.ToString(),
            response.StopReason?.ToString() ?? "",
            response.Model.ToString(),
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            response.StopDetails?.Category?.ToString());
    }

    static private string Shorten(string message) => message.Length <= 300 ? message : message[..300] + "…";
}
