using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Plugins.Common;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Network;

public enum RespondWith
{
    /// <summary>The JSON of the first input item.</summary>
    FirstItem,

    /// <summary>An array with the JSON of all input items.</summary>
    AllItems,

    /// <summary>The <see cref="RespondToWebhookNodeParameters.Body"/> parameter.</summary>
    CustomBody,

    NoBody
}

public sealed class RespondToWebhookNodeParameters
{
    [PipeParameter(Label = "Respond with", Order = 1, Expressions = false)]
    public RespondWith RespondWith { get; set; } = RespondWith.FirstItem;

    [PipeParameter(Label = "Body", Order = 2, Multiline = true, Placeholder = "{{ { ok: true, id: $json.id } }}",
        Description = "Custom body: JSON or an expression, evaluated against the first item")]
    public JsonNode? Body { get; set; }

    [PipeParameter(Label = "Status code", Order = 3)]
    [Range(100, 599)]
    public int StatusCode { get; set; } = 200;

    [PipeParameter(Label = "Headers", Order = 4)]
    public Dictionary<string, string> Headers { get; set; } = new();
}

[PipeNode(PipeBuiltInNodes.RespondToWebhook, "Respond to Webhook",
    Category = "Network",
    Description = "Answers the webhook call that started the execution (Webhook Trigger with 'Using respond node'). "
                  + "The items pass through, so the workflow can continue after responding.")]
public sealed class RespondToWebhookNode : IPipeNode<RespondToWebhookNodeParameters>
{
    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput();
        var first = items.Count > 0 ? items[0] : null;
        var parameters = context.GetParameters<RespondToWebhookNodeParameters>(first);

        var body = parameters.RespondWith switch
        {
            RespondWith.FirstItem => first?.Json.DeepClone(),
            RespondWith.AllItems => new JsonArray(items.Select(i => (JsonNode)i.Json.DeepClone()).ToArray()),
            RespondWith.CustomBody => parameters.Body?.DeepClone(),
            _ => null
        };
        var response = new PipeWebhookResponse { StatusCode = parameters.StatusCode, Body = body };
        foreach (var (name, value) in parameters.Headers.Where(h => h.Key.Length > 0))
        {
            response.Headers[name] = value;
        }

        if (PipeWebhookResponses.TryRespond(context.ExecutionId, response))
        {
            context.Logger.LogInformation("Responded to the webhook call with {StatusCode}", parameters.StatusCode);
        }
        else
        {
            // manual runs, other response modes, or a second Respond node
            context.Logger.LogInformation("No webhook call is waiting for a response; nothing sent");
        }

        return Task.FromResult(PipeNodeResult.FromMain(items));
    }
}

/// <summary>Webhook calls waiting for a Respond to Webhook node, by execution id.</summary>
static internal class PipeWebhookResponses
{
    static private readonly ConcurrentDictionary<string, TaskCompletionSource<PipeWebhookResponse>> Pending = new();

    /// <summary>Registers a waiting call; dispose the result when the call is done.</summary>
    static public Waiting Wait(string executionId)
    {
        var source = new TaskCompletionSource<PipeWebhookResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending[executionId] = source;
        return new Waiting(executionId, source.Task);
    }

    /// <summary>False if no call waits for this execution (or it already got its response).</summary>
    static public bool TryRespond(string executionId, PipeWebhookResponse response)
        => Pending.TryRemove(executionId, out var source) && source.TrySetResult(response);

    public sealed class Waiting(string executionId, Task<PipeWebhookResponse> response) : IDisposable
    {
        public Task<PipeWebhookResponse> Response { get; } = response;

        public void Dispose() => Pending.TryRemove(executionId, out _);
    }
}
