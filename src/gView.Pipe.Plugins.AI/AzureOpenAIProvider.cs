using Azure.Core;
using Azure.Identity;
using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Serialization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.AI;

/// <summary>A model deployment of Azure OpenAI over plain HTTP (ADR 0042), so the API version is exactly the configured one.</summary>
internal sealed class AzureOpenAIProvider : IPipeLlmProvider
{
    static private readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    static private readonly TokenCredential Identity = new DefaultAzureCredential();

    public string CredentialTypeId => AzureOpenAICredential.TypeId;

    public async Task<PipeLlmResponse> CompleteAsync(JsonObject credentialData, PipeLlmRequest request, CancellationToken cancellationToken)
    {
        var credential = credentialData.Deserialize<AzureOpenAICredential>(PipeJson.Options) ?? new AzureOpenAICredential();
        var url = AzureOpenAIRequests.UrlOf(credential);

        var (status, answer) = await SendAsync(credential, url, AzureOpenAIRequests.Body(request, oldTokenName: false), cancellationToken);
        // older deployments know only max_tokens
        if (status == HttpStatusCode.BadRequest && AzureOpenAIRequests.ErrorText(answer).Contains("max_completion_tokens", StringComparison.OrdinalIgnoreCase))
        {
            (status, answer) = await SendAsync(credential, url, AzureOpenAIRequests.Body(request, oldTokenName: true), cancellationToken);
        }

        return AzureOpenAIRequests.Parse(status, answer);
    }

    static private async Task<(HttpStatusCode Status, JsonObject? Answer)> SendAsync(AzureOpenAICredential credential, Uri url, JsonObject body, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        if (credential.Authentication == AzureOpenAIAuthentication.ManagedIdentity)
        {
            AccessToken token;
            try
            {
                token = await Identity.GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), cancellationToken);
            }
            catch (AuthenticationFailedException)
            {
                throw new PipeException("The sign-in to Azure failed (managed identity or developer login)");
            }
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }
        else
        {
            message.Headers.Add("api-key", string.IsNullOrWhiteSpace(credential.ApiKey)
                ? throw new PipeException("The credential has no API key")
                : credential.ApiKey.Trim());
        }

        using var response = await Http.SendAsync(message, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonObject? answer;
        try
        {
            answer = text.Length > 0 ? JsonNode.Parse(text) as JsonObject : null;
        }
        catch (JsonException)
        {
            answer = null;
        }
        return (response.StatusCode, answer);
    }
}

/// <summary>URL, body and answer of a chat completion of Azure OpenAI; public so they are tested without the service.</summary>
static public class AzureOpenAIRequests
{
    /// <summary>The URL of the chat completions of the deployment. The endpoint may be the whole URL the Azure portal shows.</summary>
    static public Uri UrlOf(AzureOpenAICredential credential)
    {
        var endpoint = (credential.Endpoint ?? "").Trim();
        var deployment = credential.Deployment?.Trim();
        var apiVersion = credential.ApiVersion?.Trim();

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new PipeException("The endpoint must be an https address, e.g. https://<resource>.openai.azure.com");
        }

        // the whole URL of a deployment: .../openai/deployments/<name>/chat/completions?api-version=...
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.FindIndex(segments, s => s.Equals("deployments", StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < segments.Length)
        {
            deployment = string.IsNullOrEmpty(deployment) ? Uri.UnescapeDataString(segments[index + 1]) : deployment;
            var fromQuery = System.Web.HttpUtility.ParseQueryString(uri.Query)["api-version"];
            apiVersion = string.IsNullOrEmpty(apiVersion) ? fromQuery : apiVersion;
        }

        if (string.IsNullOrEmpty(deployment))
        {
            throw new PipeException("The credential needs the name of the deployment");
        }
        if (string.IsNullOrEmpty(apiVersion))
        {
            apiVersion = AzureOpenAICredential.DefaultApiVersion;
        }

        var root = $"{uri.Scheme}://{uri.Authority}";
        return new Uri($"{root}/openai/deployments/{Uri.EscapeDataString(deployment)}/chat/completions?api-version={Uri.EscapeDataString(apiVersion)}");
    }

    /// <param name="oldTokenName">max_tokens instead of max_completion_tokens, for deployments that know only that.</param>
    static public JsonObject Body(PipeLlmRequest request, bool oldTokenName)
    {
        var user = new JsonArray();
        foreach (var part in request.Content)
        {
            switch (part)
            {
                case PipeLlmImage image:
                    user.Add(new JsonObject
                    {
                        ["type"] = "image_url",
                        ["image_url"] = new JsonObject { ["url"] = $"data:{image.MediaType};base64,{Convert.ToBase64String(image.Data)}" }
                    });
                    break;
                case PipeLlmDocument:
                    throw new PipeException("Azure OpenAI doesn't take PDF documents; use an Anthropic API credential, or extract the text first");
                case PipeLlmText text:
                    user.Add(new JsonObject { ["type"] = "text", ["text"] = text.Text });
                    break;
            }
        }

        var body = new JsonObject
        {
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = request.System },
                new JsonObject { ["role"] = "user", ["content"] = user }),
            [oldTokenName ? "max_tokens" : "max_completion_tokens"] = request.MaxTokens
        };

        if (request.JsonSchema is { } schema)
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = "result",
                    ["strict"] = true,
                    ["schema"] = schema.DeepClone()
                }
            };
        }
        return body;
    }

    /// <summary>The answer, with the finish reasons of OpenAI mapped to those of the node.</summary>
    static public PipeLlmResponse Parse(HttpStatusCode status, JsonObject? answer)
    {
        var error = answer?["error"];
        if ((int)status >= 400 || error is not null)
        {
            var code = error?["code"]?.ToString() ?? error?["innererror"]?["code"]?.ToString();
            // a request the content filter stopped is a refusal, like a declined request of Claude
            if (code is "content_filter" or "ResponsibleAIPolicyViolation")
            {
                return new PipeLlmResponse("", "refusal", "", 0, 0, "content_filter");
            }
            throw new PipeException($"Azure OpenAI answered {(int)status}{(code is null ? "" : $" ({code})")}: {Shorten(ErrorText(answer))}");
        }

        var choice = answer?["choices"]?[0];
        var message = choice?["message"];
        var finish = choice?["finish_reason"]?.ToString();
        var refusal = message?["refusal"]?.ToString();
        var usage = answer?["usage"];

        var stopReason = !string.IsNullOrEmpty(refusal) || finish == "content_filter"
            ? "refusal"
            : finish == "length" ? "max_tokens" : "end_turn";

        return new PipeLlmResponse(
            message?["content"]?.ToString() ?? "",
            stopReason,
            answer?["model"]?.ToString() ?? "",
            Count(usage?["prompt_tokens"]),
            Count(usage?["completion_tokens"]),
            stopReason == "refusal" ? (finish == "content_filter" ? "content_filter" : "refusal") : null);
    }

    // a number from the service, whatever its JSON type is
    static private long Count(JsonNode? node)
        => long.TryParse(node?.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;

    static public string ErrorText(JsonObject? answer) => answer?["error"]?["message"]?.ToString() ?? "";

    static private string Shorten(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
