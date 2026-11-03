using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Audit;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Security;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Abstraction.Variables;
using gView.Pipe.Abstraction.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.AspNetCore.Api;

/// <summary>Body of <c>POST /workflows/{id}/run</c>.</summary>
public sealed class PipeApiRunRequest
{
    /// <summary>Input items of the start nodes; default one empty item.</summary>
    public List<JsonObject>? Items { get; set; }

    /// <summary>Environment to run in; default the workflow's.</summary>
    public string? Environment { get; set; }

    /// <summary>Wait for the end of the run (up to <see cref="TimeoutSeconds"/>) and answer with its output.</summary>
    public bool Wait { get; set; }

    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>Body of <c>POST /executions/{workflowId}/{id}/replay</c>.</summary>
public sealed class PipeApiReplayRequest
{
    /// <summary>Run this node and everything after it again; default the whole workflow.</summary>
    public string? FromNodeId { get; set; }
}

/// <summary>The REST API under <c>/api/v1</c> (ADR 0020).</summary>
static public class PipeApiEndpoints
{
    public const string Prefix = "/api/v1";
    public const string DocumentName = "v1";

    private const string KeyItem = "gview-pipe.api-key";

    static public RouteGroupBuilder Map(IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup(Prefix)
            .WithGroupName(DocumentName)
            .AllowAnonymous()
            .DisableAntiforgery()
            .AddEndpointFilter(async (context, next) =>
            {
                var right = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<PipeApiRightMetadata>()?.Right ?? PipeApiRights.Read;
                var (key, problem) = await context.HttpContext.RequestServices.GetRequiredService<PipeApiAuthenticator>()
                    .AuthenticateAsync(context.HttpContext, right);
                if (problem is not null)
                {
                    return problem;
                }
                context.HttpContext.Items[KeyItem] = key;
                return await next(context);
            });

        // (Delegate): a handler with only an HttpContext would bind as RequestDelegate, and its IResult would be dropped
        api.MapGet("/workflows", (Delegate)ListWorkflowsAsync).Need(PipeApiRights.Read).WithSummary("Lists the workflows");
        api.MapGet("/workflows/{id}", GetWorkflowAsync).Need(PipeApiRights.Read).WithSummary("Gets a workflow; the ETag is its version for If-Match");
        api.MapPut("/workflows/{id}", PutWorkflowAsync).Need(PipeApiRights.Deploy).WithSummary("Creates or replaces a workflow");
        api.MapDelete("/workflows/{id}", DeleteWorkflowAsync).Need(PipeApiRights.Deploy).WithSummary("Deletes a workflow");
        api.MapGet("/workflows/{id}/versions", ListVersionsAsync).Need(PipeApiRights.Read).WithSummary("Lists the saved versions of a workflow, newest first");
        api.MapPost("/workflows/{id}/activate", (string id, HttpContext context) => SetActiveAsync(id, true, context)).Need(PipeApiRights.Deploy).WithSummary("Activates the triggers of a workflow");
        api.MapPost("/workflows/{id}/deactivate", (string id, HttpContext context) => SetActiveAsync(id, false, context)).Need(PipeApiRights.Deploy).WithSummary("Deactivates the triggers of a workflow");
        api.MapPost("/workflows/{id}/run", RunAsync).Need(PipeApiRights.Run).WithSummary("Starts a run");
        api.MapGet("/executions", ListExecutionsAsync).Need(PipeApiRights.Read).WithSummary("Lists executions, newest first");
        api.MapGet("/executions/{workflowId}/{id}", GetExecutionAsync).Need(PipeApiRights.Read).WithSummary("Gets an execution with its node runs");
        api.MapPost("/executions/{workflowId}/{id}/cancel", CancelAsync).Need(PipeApiRights.Run).WithSummary("Stops a running or queued execution");
        api.MapPost("/executions/{workflowId}/{id}/replay", ReplayAsync).Need(PipeApiRights.Run).WithSummary("Runs a stored execution again");
        api.MapGet("/groups", (Delegate)ListGroupsAsync).Need(PipeApiRights.Read).WithSummary("Lists the workflow groups");
        PipePortalEndpoints.Map(api);
        api.MapGet("/audit", AuditAsync).Need(PipeApiRights.Read).WithSummary("The audit log, newest first; only for keys not limited to groups");
        return api;
    }

    static private TBuilder Need<TBuilder>(this TBuilder builder, PipeApiRights right) where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(new PipeApiRightMetadata(right));

    static internal PipeApiKey Key(HttpContext context) => (PipeApiKey)context.Items[KeyItem]!;

    static internal IResult Json(object? value, int status = StatusCodes.Status200OK) => Results.Json(value, PipeJson.Options, statusCode: status);

    static internal IResult Problem(int status, string title, object? details = null)
        => Results.Problem(statusCode: status, title: title, extensions: details is null ? null : new Dictionary<string, object?> { ["details"] = details });

    static internal IResult NotFound(string what) => Problem(StatusCodes.Status404NotFound, $"{what} not found");

    /// <summary>The workflow, when it exists and the key covers its group; a limited key never learns about others.</summary>
    static private async Task<PipeWorkflow?> VisibleWorkflowAsync(string id, HttpContext context)
    {
        var workflow = await Services<IPipeWorkflowStore>(context).GetAsync(id, context.RequestAborted);
        return workflow is not null && Key(context).Covers(workflow.GroupId) ? workflow : null;
    }

    static internal T Services<T>(HttpContext context) where T : notnull => context.RequestServices.GetRequiredService<T>();

    /// <summary>An audit entry with the key as actor (ADR 0022).</summary>
    static internal Task WriteAuditAsync(HttpContext context, PipeAuditArea area, string action, string? objectId, string? objectName, string? details = null)
        => Services<IPipeAuditLog>(context).WriteAsync(new PipeAuditEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Actor = $"api:{Key(context).Name}",
            Area = area,
            Action = action,
            ObjectId = objectId,
            ObjectName = objectName,
            Details = details
        });

    static private string ETag(PipeWorkflow workflow)
        => $"\"{(workflow.UpdatedAt ?? workflow.CreatedAt ?? DateTimeOffset.MinValue).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}\"";

    // ---- workflows ----

    static private async Task<IResult> ListWorkflowsAsync(HttpContext context)
    {
        var key = Key(context);
        var triggers = Services<IPipeTriggerManager>(context);
        var workflows = (await Services<IPipeWorkflowStore>(context).ListAsync(context.RequestAborted))
            .Where(w => key.Covers(w.GroupId))
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .Select(w => new { w.Id, w.Name, w.Description, w.GroupId, w.Active, Armed = triggers.IsActive(w.Id), w.Environment, w.UpdatedAt })
            .ToList();
        return Json(workflows);
    }

    static private async Task<IResult> GetWorkflowAsync(string id, HttpContext context)
    {
        if (await VisibleWorkflowAsync(id, context) is not { } workflow)
        {
            return NotFound($"Workflow '{id}'");
        }
        context.Response.Headers.ETag = ETag(workflow);
        return Json(workflow);
    }

    static private async Task<IResult> ListVersionsAsync(string id, HttpContext context)
    {
        if (await VisibleWorkflowAsync(id, context) is null)
        {
            return NotFound($"Workflow '{id}'");
        }
        var versions = await context.RequestServices.GetRequiredService<IPipeWorkflowVersionStore>().ListAsync(id, context.RequestAborted);
        return Json(versions);
    }

    static private async Task<IResult> PutWorkflowAsync(string id, HttpContext context)
    {
        PipeWorkflow? workflow;
        try
        {
            workflow = await context.Request.ReadFromJsonAsync<PipeWorkflow>(PipeJson.Options, context.RequestAborted);
        }
        catch (JsonException ex)
        {
            return Problem(StatusCodes.Status400BadRequest, $"The body isn't a workflow: {ex.Message}");
        }
        if (workflow is null)
        {
            return Problem(StatusCodes.Status400BadRequest, "The body is empty");
        }
        workflow.Id = id;

        var key = Key(context);
        var store = Services<IPipeWorkflowStore>(context);
        var existing = await store.GetAsync(id, context.RequestAborted);
        if (!key.Covers(workflow.GroupId) || (existing is not null && !key.Covers(existing.GroupId)))
        {
            return Problem(StatusCodes.Status403Forbidden, $"The API key '{key.Name}' may only deploy workflows of its groups");
        }
        if (context.Request.Headers.IfMatch.ToString() is { Length: > 0 } ifMatch && (existing is null || ifMatch != ETag(existing)))
        {
            return Problem(StatusCodes.Status412PreconditionFailed, "The workflow was changed since it was read; get it again");
        }

        var validation = await Services<IPipeWorkflowValidator>(context).ValidateAsync(workflow, context.RequestAborted);
        if (!validation.IsValid)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "The workflow isn't valid", new { validation.Errors, validation.Warnings });
        }

        workflow.CreatedAt = existing?.CreatedAt ?? workflow.CreatedAt;
        await store.SaveAsync(workflow, context.RequestAborted);
        await WriteAuditAsync(context, PipeAuditArea.Workflow, existing is null ? "created" : "deployed", workflow.Id, workflow.Name,
            existing is null ? null : PipeWorkflowChanges.Describe(existing, workflow));
        var warnings = validation.Warnings.Concat(await ArmAsync(workflow, context)).ToList();

        context.Response.Headers.ETag = ETag(workflow);
        return Json(new { workflow.Id, Created = existing is null, workflow.Active, Warnings = warnings }, existing is null ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }

    /// <summary>Arms the triggers of an active workflow again (they may have changed), or disarms an inactive one.</summary>
    static private async Task<IReadOnlyList<string>> ArmAsync(PipeWorkflow workflow, HttpContext context)
    {
        var triggers = Services<IPipeTriggerManager>(context);
        await triggers.DeactivateAsync(workflow.Id);
        return workflow.Active ? await triggers.ActivateAsync(workflow, context.RequestAborted) : [];
    }

    static private async Task<IResult> DeleteWorkflowAsync(string id, HttpContext context)
    {
        if (await VisibleWorkflowAsync(id, context) is null)
        {
            return NotFound($"Workflow '{id}'");
        }
        await Services<IPipeTriggerManager>(context).DeactivateAsync(id);
        await Services<IPipeWorkflowStore>(context).DeleteAsync(id, context.RequestAborted);
        await WriteAuditAsync(context, PipeAuditArea.Workflow, "deleted", id, null);
        return Results.NoContent();
    }

    static private async Task<IResult> SetActiveAsync(string id, bool active, HttpContext context)
    {
        if (await VisibleWorkflowAsync(id, context) is not { } workflow)
        {
            return NotFound($"Workflow '{id}'");
        }
        workflow.Active = active;
        await Services<IPipeWorkflowStore>(context).SaveAsync(workflow, context.RequestAborted);
        var problems = await ArmAsync(workflow, context);
        await WriteAuditAsync(context, PipeAuditArea.Workflow, active ? "activated" : "deactivated", workflow.Id, workflow.Name);
        return Json(new { workflow.Id, workflow.Active, Warnings = problems });
    }

    // ---- runs ----

    static private async Task<IResult> RunAsync(string id, HttpContext context)
    {
        if (await VisibleWorkflowAsync(id, context) is not { } workflow)
        {
            return NotFound($"Workflow '{id}'");
        }

        var body = context.Request.ContentLength is > 0 || context.Request.Headers.TransferEncoding.Count > 0
            ? await context.Request.ReadFromJsonAsync<PipeApiRunRequest>(PipeJson.Options, context.RequestAborted) ?? new PipeApiRunRequest()
            : new PipeApiRunRequest();

        string? environment = null;
        if (!string.IsNullOrWhiteSpace(body.Environment))
        {
            environment = PipeEnvironments.Normalize(body.Environment);
            if (!Services<IPipeEnvironments>(context).All.Contains(environment, StringComparer.OrdinalIgnoreCase))
            {
                return Problem(StatusCodes.Status400BadRequest, $"Unknown environment '{body.Environment}'");
            }
        }

        var ticket = new PipeQueueTicket();
        var executionId = Guid.NewGuid().ToString("N");
        // the run must not stop when the caller disconnects
        var run = Services<IPipeWorkflowRunner>(context).RunAsync(workflow, new PipeExecutionRequest
        {
            Mode = PipeExecutionMode.Api,
            ExecutionId = executionId,
            Environment = environment,
            InputItems = body.Items?.Select(i => new PipeItem(i)).ToList(),
            StartedBy = $"api:{Key(context).Name}",
            QueueTicket = ticket
        }, CancellationToken.None);

        if (ticket.State == PipeQueueState.Rejected)
        {
            return Problem(StatusCodes.Status429TooManyRequests, "The workflow is already running (max concurrent runs reached) and its queue is full");
        }
        await WriteAuditAsync(context, PipeAuditArea.Execution, "started", workflow.Id, workflow.Name, $"execution {executionId}");
        if (!body.Wait)
        {
            return Json(new { ExecutionId = executionId, Status = ticket.State == PipeQueueState.Queued ? "queued" : "started" }, StatusCodes.Status202Accepted);
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(body.TimeoutSeconds, 1, 3600));
        if (await Task.WhenAny(run, Task.Delay(timeout, context.RequestAborted)) != run)
        {
            return Json(new { ExecutionId = executionId, Status = ticket.State == PipeQueueState.Queued ? "queued" : "running" }, StatusCodes.Status202Accepted);
        }
        return await run is { } execution
            ? Json(Result(execution))
            : Problem(StatusCodes.Status429TooManyRequests, "The run left the queue before it started");
    }

    /// <summary>Status, error and the items of the node that finished last, as a webhook "when finished" answers.</summary>
    static internal object Result(PipeExecution execution)
    {
        var last = execution.NodeRuns.Values
            .Where(r => r.FinishedAt is not null && r.Outputs is not null)
            .OrderBy(r => r.FinishedAt)
            .LastOrDefault();
        return new
        {
            ExecutionId = execution.Id,
            execution.Status,
            execution.Error,
            ErrorNode = execution.ErrorNodeId is { } nodeId ? execution.NodeRuns.GetValueOrDefault(nodeId)?.NodeName : null,
            Node = last?.NodeName,
            Items = last?.Outputs?.GetValueOrDefault(PipePorts.Main)?.Select(i => i.Json) ?? []
        };
    }

    static private async Task<IResult> ListExecutionsAsync(HttpContext context, string? workflowId, string? status, string? mode,
        DateTimeOffset? from, DateTimeOffset? before, int? limit)
    {
        var key = Key(context);
        var visible = (await Services<IPipeWorkflowStore>(context).ListAsync(context.RequestAborted))
            .Where(w => key.Covers(w.GroupId))
            .Select(w => w.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (workflowId is not null && !visible.Contains(workflowId))
        {
            return NotFound($"Workflow '{workflowId}'");
        }

        PipeExecutionStatus[]? statuses = null;
        PipeExecutionMode[]? modes = null;
        try
        {
            statuses = status is null ? null : [Enum.Parse<PipeExecutionStatus>(status, ignoreCase: true)];
            modes = mode is null ? null : [Enum.Parse<PipeExecutionMode>(mode, ignoreCase: true)];
        }
        catch (ArgumentException)
        {
            return Problem(StatusCodes.Status400BadRequest, "Unknown status or mode");
        }

        var executions = await Services<IPipeExecutionStore>(context).QueryAsync(new PipeExecutionQuery
        {
            // a key limited to groups sees only their workflows; ids of others don't match anything
            WorkflowIds = workflowId is not null ? [workflowId] : key.Groups is { Count: > 0 } ? visible.DefaultIfEmpty("\0").ToList() : null,
            Statuses = statuses,
            Modes = modes,
            From = from,
            Before = before,
            MaxCount = Math.Clamp(limit ?? 50, 1, 500)
        }, context.RequestAborted);
        return Json(executions);
    }

    static private async Task<IResult> GetExecutionAsync(string workflowId, string id, HttpContext context)
    {
        if (await VisibleWorkflowAsync(workflowId, context) is null)
        {
            return NotFound($"Workflow '{workflowId}'");
        }
        if (await Services<IPipeExecutionStore>(context).GetAsync(workflowId, id, context.RequestAborted) is { } execution)
        {
            return Json(execution);
        }
        // not stored yet: running or waiting in the queue
        return Services<IPipeRunningExecutions>(context).Find(id) is { } running && running.WorkflowId == workflowId
            ? Json(new { ExecutionId = id, WorkflowId = workflowId, running.Status, running.StartedAt, running.QueuePosition })
            : NotFound($"Execution '{id}'");
    }

    static private async Task<IResult> CancelAsync(string workflowId, string id, HttpContext context)
    {
        if (await VisibleWorkflowAsync(workflowId, context) is null)
        {
            return NotFound($"Workflow '{workflowId}'");
        }
        var running = Services<IPipeRunningExecutions>(context);
        if (running.Find(id) is not { WorkflowId: var owner } || owner != workflowId || !running.Cancel(id))
        {
            return Problem(StatusCodes.Status409Conflict, $"Execution '{id}' isn't running");
        }
        await WriteAuditAsync(context, PipeAuditArea.Execution, "stopped", workflowId, null, $"execution {id}");
        return Results.Accepted();
    }

    static private async Task<IResult> ReplayAsync(string workflowId, string id, HttpContext context)
    {
        if (await VisibleWorkflowAsync(workflowId, context) is null)
        {
            return NotFound($"Workflow '{workflowId}'");
        }
        var body = context.Request.ContentLength is > 0
            ? await context.Request.ReadFromJsonAsync<PipeApiReplayRequest>(PipeJson.Options, context.RequestAborted) ?? new PipeApiReplayRequest()
            : new PipeApiReplayRequest();

        // the replay gets its id when it starts; it may wait for a slot first
        var started = new StartedObserver();
        var replay = Services<IPipeWorkflowRunner>(context).ReplayAsync(workflowId, id, body.FromNodeId, started, CancellationToken.None);
        var first = await Task.WhenAny(replay, started.Started, Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted));
        if (first == replay && replay.IsFaulted)
        {
            return replay.Exception!.InnerException is PipeException ex
                ? Problem(ex.Message.Contains("not found") ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict, ex.Message)
                : throw replay.Exception!.InnerException!;
        }
        await WriteAuditAsync(context, PipeAuditArea.Execution, "replayed", workflowId, null,
            $"execution {id}" + (started.Started.IsCompleted ? $" as {await started.Started}" : ""));
        return started.Started.IsCompleted
            ? Json(new { ExecutionId = await started.Started, ReplayOf = id, Status = "started" }, StatusCodes.Status202Accepted)
            : Json(new { ReplayOf = id, Status = "queued" }, StatusCodes.Status202Accepted);
    }

    static private async Task<IResult> AuditAsync(HttpContext context, DateTimeOffset? from, DateTimeOffset? before, string? actor,
        string? area, string? objectId, int? limit)
    {
        if (Key(context).Groups is { Count: > 0 })
        {
            return Problem(StatusCodes.Status403Forbidden, "The audit log is only for API keys that aren't limited to groups");
        }
        PipeAuditArea? parsed = null;
        if (area is not null)
        {
            if (!Enum.TryParse<PipeAuditArea>(area, ignoreCase: true, out var value))
            {
                return Problem(StatusCodes.Status400BadRequest, $"Unknown area '{area}'");
            }
            parsed = value;
        }
        return Json(await Services<IPipeAuditLog>(context).QueryAsync(new PipeAuditQuery
        {
            From = from,
            Before = before,
            Actor = actor,
            Area = parsed,
            ObjectId = objectId,
            MaxCount = Math.Clamp(limit ?? 100, 1, 1000)
        }, context.RequestAborted));
    }

    static private async Task<IResult> ListGroupsAsync(HttpContext context)
    {
        var key = Key(context);
        var groups = (await Services<IPipeWorkflowGroupStore>(context).ListAsync(context.RequestAborted))
            .Where(g => key.Covers(g.Id))
            .Select(g => new { g.Id, g.Name, g.Description, g.Environment, g.ErrorWorkflowId })
            .ToList();
        return Json(groups);
    }

    /// <summary>Hears the id of a replay once it starts.</summary>
    private sealed class StartedObserver : IPipeExecutionObserver
    {
        private readonly TaskCompletionSource<string> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> Started => _started.Task;

        public Task OnExecutionStartedAsync(PipeExecution execution)
        {
            _started.TrySetResult(execution.Id);
            return Task.CompletedTask;
        }
    }
}
