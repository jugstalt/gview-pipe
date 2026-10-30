using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Triggers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;

namespace gView.Pipe.AspNetCore.Webhooks;

/// <summary>Turns HTTP requests into <see cref="PipeWebhookRequest"/>s and the router's responses back into HTTP.</summary>
static internal class PipeWebhookEndpoint
{
    // responses are application/json, never HTML: no need to escape quotes, '+', umlauts, …
    static private readonly JsonSerializerOptions ResponseJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    static public async Task HandleAsync(HttpContext context, PipeWebhookEndpointOptions options)
    {
        var request = context.Request;

        if (request.ContentLength > options.MaxBodyBytes)
        {
            await WriteAsync(context, PipeWebhookResponse.Error(413, $"Request body is larger than {options.MaxBodyBytes} bytes"));
            return;
        }
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
        {
            bodySize.MaxRequestBodySize = options.MaxBodyBytes;
        }

        string path;
        try
        {
            path = PipeWebhookPaths.Normalize(context.GetRouteValue("path") as string ?? "");
        }
        catch (PipeException ex)
        {
            await WriteAsync(context, PipeWebhookResponse.Error(404, ex.Message));
            return;
        }

        PipeWebhookRequest webhookRequest;
        try
        {
            webhookRequest = await ReadRequestAsync(request, path, options, context.RequestAborted);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await WriteAsync(context, PipeWebhookResponse.Error(413, $"Request body is larger than {options.MaxBodyBytes} bytes"));
            return;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or BadHttpRequestException)
        {
            await WriteAsync(context, PipeWebhookResponse.Error(400, $"Can't read the request: {ex.Message}"));
            return;
        }

        var router = context.RequestServices.GetRequiredService<IPipeWebhookRouter>();
        var response = await router.HandleAsync(webhookRequest, context.RequestAborted);
        await WriteAsync(context, response);
    }

    static private async Task<PipeWebhookRequest> ReadRequestAsync(HttpRequest request, string path, PipeWebhookEndpointOptions options, CancellationToken cancellationToken)
    {
        var files = new List<PipeWebhookFile>();
        JsonNode? body;

        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = options.MaxBodyBytes }, cancellationToken);
            var fields = new JsonObject();
            foreach (var (key, value) in form)
            {
                fields[key] = value.Count == 1
                    ? JsonValue.Create(value.ToString())
                    : new JsonArray(value.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
            }

            foreach (var file in form.Files)
            {
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, cancellationToken);
                files.Add(new PipeWebhookFile(file.Name, file.FileName, file.ContentType ?? "application/octet-stream", buffer.ToArray()));
            }

            body = fields;
        }
        else
        {
            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            var text = await reader.ReadToEndAsync(cancellationToken);
            body = text.Length == 0
                ? null
                : request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true
                    ? JsonNode.Parse(text)
                    : JsonValue.Create(text);
        }

        var webhookRequest = new PipeWebhookRequest
        {
            Method = request.Method.ToUpperInvariant(),
            Path = path,
            ContentType = request.ContentType,
            Body = body,
            Files = files
        };
        foreach (var (key, value) in request.Query)
        {
            webhookRequest.Query[key] = value.ToString();
        }
        foreach (var (key, value) in request.Headers)
        {
            webhookRequest.Headers[key.ToLowerInvariant()] = value.ToString();
        }

        return webhookRequest;
    }

    static private async Task WriteAsync(HttpContext context, PipeWebhookResponse response)
    {
        context.Response.StatusCode = response.StatusCode;
        foreach (var (name, value) in response.Headers)
        {
            context.Response.Headers[name] = value;
        }

        if (response.Body is not null)
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(response.Body.ToJsonString(ResponseJson), context.RequestAborted);
        }
    }
}
