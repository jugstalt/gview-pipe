using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Audit;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Portal;
using gView.Pipe.Abstraction.Security;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Abstraction.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.AspNetCore.Api;

/// <summary>Body of <c>POST /portal/workflows/{id}/run</c>.</summary>
public sealed class PipePortalRunRequest
{
    /// <summary>The values of the form.</summary>
    public JsonObject? Values { get; set; }

    /// <summary>Overrides the default mode of the workflow, if it allows a choice.</summary>
    public PipePortalRunMode? Mode { get; set; }

    /// <summary>Address for the mail when the run has ended (ADR 0029).</summary>
    public string? NotifyEmail { get; set; }
}

/// <summary>Remembers who started the runs that are still running in this process (ADR 0027).</summary>
public sealed class PipePortalRuns
{
    static private readonly TimeSpan Keep = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, (string User, string WorkflowId, DateTimeOffset Since)> _runs = new();

    public void Add(string executionId, string user, string workflowId)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var old in _runs.Where(r => now - r.Value.Since > Keep).Select(r => r.Key).ToList())
        {
            _runs.TryRemove(old, out _);
        }
        _runs[executionId] = (user, workflowId, now);
    }

    public string? OwnerOf(string executionId) => _runs.TryGetValue(executionId, out var run) ? run.User : null;
}

/// <summary>
/// The endpoints for the user portal under <c>/api/v1/portal</c> (ADR 0027). They need a key with the right
/// <see cref="PipeApiRights.Portal"/>, which names the user it acts for in <c>X-Pipe-On-Behalf-Of</c>.
/// </summary>
static public class PipePortalEndpoints
{
    public const string UserHeader = "X-Pipe-On-Behalf-Of";
    public const string UserPrefix = "portal:";
    public const int DefaultMaxRunsPerUser = 3;

    static internal void Map(RouteGroupBuilder api)
    {
        var portal = api.MapGroup("/portal").WithMetadata(new PipeApiRightMetadata(PipeApiRights.Portal));
        portal.MapGet("/workflows", (Delegate)ListWorkflowsAsync).WithSummary("Lists the published workflows with their forms");
        portal.MapPost("/workflows/{id}/run", RunAsync).WithSummary("Starts a run for the user; the form values are checked");
        portal.MapGet("/executions", (Delegate)ListExecutionsAsync).WithSummary("Lists the runs of the user, newest first");
        portal.MapGet("/executions/{workflowId}/{id}", GetExecutionAsync).WithSummary("Gets a run of the user with its result");
        portal.MapPost("/executions/{workflowId}/{id}/cancel", CancelAsync).WithSummary("Stops a run of the user");
    }

    /// <summary>The user the key acts for, or null when the header is missing or unusable.</summary>
    static private string? User(HttpContext context)
    {
        var user = context.Request.Headers[UserHeader].ToString().Trim();
        return user.Length is > 0 and <= 200 && !user.Any(char.IsControl) ? user : null;
    }

    static private IResult NoUser() => PipeApiEndpoints.Problem(StatusCodes.Status400BadRequest, $"The header {UserHeader} names the user the request is for");

    /// <summary>The form of a published workflow: from its Form Trigger, or empty when it has none.</summary>
    static private PipeForm FormOf(PipeWorkflow workflow, HttpContext context)
    {
        var registry = PipeApiEndpoints.Services<IPipeNodeRegistry>(context);
        foreach (var node in workflow.Nodes.Where(n => !n.Disabled))
        {
            if (registry.Find(node.Type) is { } descriptor && typeof(IPipeFormTrigger).IsAssignableFrom(descriptor.NodeType))
            {
                var trigger = (IPipeFormTrigger)ActivatorUtilities.CreateInstance(context.RequestServices, descriptor.NodeType);
                try
                {
                    return trigger.GetForm(node.Parameters);
                }
                finally
                {
                    (trigger as IDisposable)?.Dispose();
                }
            }
        }
        return new PipeForm();
    }

    /// <summary>The nodes of the workflow that produce a result for the portal.</summary>
    static private IEnumerable<PipeWorkflowNode> ResultNodes(PipeWorkflow workflow, HttpContext context)
    {
        var registry = PipeApiEndpoints.Services<IPipeNodeRegistry>(context);
        return workflow.Nodes.Where(n => registry.Find(n.Type) is { } d && typeof(IPipeResultNode).IsAssignableFrom(d.NodeType));
    }

    /// <summary>The published workflow, when the key covers its group; others are not there for the portal.</summary>
    static private async Task<PipeWorkflow?> PublishedAsync(string id, HttpContext context)
    {
        var workflow = await PipeApiEndpoints.Services<IPipeWorkflowStore>(context).GetAsync(id, context.RequestAborted);
        return workflow is { Settings.Portal.Published: true } && PipeApiEndpoints.Key(context).Covers(workflow.GroupId) ? workflow : null;
    }

    static private async Task<IResult> ListWorkflowsAsync(HttpContext context)
    {
        if (User(context) is null)
        {
            return NoUser();
        }
        var key = PipeApiEndpoints.Key(context);
        var groups = (await PipeApiEndpoints.Services<IPipeWorkflowGroupStore>(context).ListAsync(context.RequestAborted))
            .ToDictionary(g => g.Id, g => g.Name, StringComparer.Ordinal);
        var workflows = (await PipeApiEndpoints.Services<IPipeWorkflowStore>(context).ListAsync(context.RequestAborted))
            .Where(w => w.Settings.Portal is { Published: true } && key.Covers(w.GroupId))
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .Select(w => new
            {
                w.Id,
                w.Name,
                w.Description,
                Category = w.GroupId is { } g && groups.TryGetValue(g, out var name) ? name : null,
                CategoryId = w.GroupId is { } id && groups.ContainsKey(id) ? id : null,
                Form = FormOf(w, context).Fields.Select(f => new { f.Name, f.Label, f.Description, f.Placeholder, f.Type, f.Required, f.Default, f.Options, f.Pattern, f.PatternMessage }),
                DefaultMode = w.Settings.Portal!.DefaultMode,
                w.Settings.Portal.AllowModeChoice,
                w.Settings.Portal.WaitTimeoutSeconds,
                w.Settings.Portal.AllowCancel
            })
            .ToList();
        return PipeApiEndpoints.Json(workflows);
    }

    static private async Task<IResult> RunAsync(string id, HttpContext context)
    {
        if (User(context) is not { } user)
        {
            return NoUser();
        }
        if (await PublishedAsync(id, context) is not { } workflow)
        {
            return PipeApiEndpoints.NotFound($"Workflow '{id}'");
        }

        PipePortalRunRequest body;
        try
        {
            body = context.Request.ContentLength is > 0 || context.Request.Headers.TransferEncoding.Count > 0
                ? await context.Request.ReadFromJsonAsync<PipePortalRunRequest>(PipeJson.Options, context.RequestAborted) ?? new PipePortalRunRequest()
                : new PipePortalRunRequest();
        }
        catch (JsonException ex)
        {
            return PipeApiEndpoints.Problem(StatusCodes.Status400BadRequest, $"The body isn't valid: {ex.Message}");
        }

        // the form is checked here, whatever the portal did
        var (item, problems) = FormOf(workflow, context).Validate(body.Values);
        if (problems.Count > 0)
        {
            return PipeApiEndpoints.Problem(StatusCodes.Status422UnprocessableEntity, "The values don't fit the form", problems);
        }

        var settings = workflow.Settings.Portal!;
        var startedBy = UserPrefix + user;
        var running = PipeApiEndpoints.Services<IPipeRunningExecutions>(context).List().Where(r => r.IsRunning).ToList();
        if (settings.SingleRun && running.Any(r => r.WorkflowId == workflow.Id))
        {
            return PipeApiEndpoints.Problem(StatusCodes.Status429TooManyRequests, "This workflow is running already; try again when it has ended");
        }
        var owners = PipeApiEndpoints.Services<PipePortalRuns>(context);
        var limit = PipeApiEndpoints.Services<IConfiguration>(context).GetValue("Pipe:Portal:MaxRunsPerUser", DefaultMaxRunsPerUser);
        if (limit > 0 && running.Count(r => owners.OwnerOf(r.ExecutionId) == user) >= limit)
        {
            return PipeApiEndpoints.Problem(StatusCodes.Status429TooManyRequests, $"You have {limit} runs going already; wait for one to end");
        }

        var mode = settings.AllowModeChoice ? body.Mode ?? settings.DefaultMode : settings.DefaultMode;
        var ticket = new PipeQueueTicket();
        var executionId = Guid.NewGuid().ToString("N");
        owners.Add(executionId, user, workflow.Id);
        var run = PipeApiEndpoints.Services<IPipeWorkflowRunner>(context).RunAsync(workflow, new PipeExecutionRequest
        {
            Mode = PipeExecutionMode.Api,
            ExecutionId = executionId,
            InputItems = [new PipeItem(item)],
            StartedBy = startedBy,
            QueueTicket = ticket
        }, CancellationToken.None);

        if (ticket.State == PipeQueueState.Rejected)
        {
            return PipeApiEndpoints.Problem(StatusCodes.Status429TooManyRequests, "The workflow is busy and its queue is full; try again later");
        }
        await PipeApiEndpoints.WriteAuditAsync(context, PipeAuditArea.Execution, "started", workflow.Id, workflow.Name, $"execution {executionId} for {startedBy}");

        if (mode == PipePortalRunMode.Wait)
        {
            var timeout = TimeSpan.FromSeconds(Math.Clamp(settings.WaitTimeoutSeconds, 1, 3600));
            if (await Task.WhenAny(run, Task.Delay(timeout, context.RequestAborted)) == run && await run is { } finished)
            {
                return PipeApiEndpoints.Json(View(workflow, finished, context));
            }
        }
        return PipeApiEndpoints.Json(new { ExecutionId = executionId, WorkflowId = workflow.Id, Status = ticket.State == PipeQueueState.Queued ? "queued" : "running" },
            StatusCodes.Status202Accepted);
    }

    /// <summary>What the portal shows of a finished run: status, the result of the Result node, a safe error message.</summary>
    static private object View(PipeWorkflow? workflow, PipeExecution execution, HttpContext context)
    {
        var resultNodes = workflow is null ? [] : ResultNodes(workflow, context).ToList();
        PipeResultView? result = null;
        if (execution.Status == PipeExecutionStatus.Success)
        {
            var run = resultNodes
                .Select(n => execution.NodeRuns.GetValueOrDefault(n.Id))
                .Where(r => r?.FinishedAt is not null && r.Outputs is not null)
                .OrderBy(r => r!.FinishedAt)
                .LastOrDefault();
            if (run?.Outputs?.GetValueOrDefault(PipePorts.Main)?.FirstOrDefault() is { } item)
            {
                result = item.Json.Deserialize<PipeResultView>(PipeJson.Options);
                result!.ErrorMessage = null;
            }
        }

        string? error = null;
        if (execution.Status == PipeExecutionStatus.Error)
        {
            // details of errors stay with the admins; the author may give a text
            error = resultNodes.Select(n => (string?)n.Parameters["errorMessage"]).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
                    ?? "The run failed. Tell your administrator the number of the run.";
        }

        return new
        {
            ExecutionId = execution.Id,
            execution.WorkflowId,
            execution.WorkflowName,
            Status = execution.Status.ToString().ToLowerInvariant(),
            execution.StartedAt,
            execution.FinishedAt,
            Result = result,
            Error = error
        };
    }

    /// <summary>True when the run was started for the user.</summary>
    static private async Task<bool> OwnsAsync(string user, string workflowId, string executionId, HttpContext context)
    {
        if (PipeApiEndpoints.Services<PipePortalRuns>(context).OwnerOf(executionId) == user)
        {
            return true;
        }
        var stored = await PipeApiEndpoints.Services<IPipeExecutionStore>(context).GetAsync(workflowId, executionId, context.RequestAborted);
        return stored is not null && stored.StartedBy == UserPrefix + user;
    }

    static private async Task<IResult> ListExecutionsAsync(HttpContext context, int? limit)
    {
        if (User(context) is not { } user)
        {
            return NoUser();
        }
        var key = PipeApiEndpoints.Key(context);
        var workflows = (await PipeApiEndpoints.Services<IPipeWorkflowStore>(context).ListAsync(context.RequestAborted))
            .Where(w => key.Covers(w.GroupId))
            .ToDictionary(w => w.Id, StringComparer.Ordinal);
        var executions = await PipeApiEndpoints.Services<IPipeExecutionStore>(context).QueryAsync(new PipeExecutionQuery
        {
            StartedBy = UserPrefix + user,
            Modes = [PipeExecutionMode.Api],
            MaxCount = Math.Clamp(limit ?? 50, 1, 200)
        }, context.RequestAborted);

        var owners = PipeApiEndpoints.Services<PipePortalRuns>(context);
        var stored = executions.Select(e => e.Id).ToHashSet();
        var active = PipeApiEndpoints.Services<IPipeRunningExecutions>(context).List()
            .Where(r => r.IsRunning && owners.OwnerOf(r.ExecutionId) == user && workflows.ContainsKey(r.WorkflowId) && !stored.Contains(r.ExecutionId))
            .Select(r => new
            {
                r.ExecutionId,
                r.WorkflowId,
                r.WorkflowName,
                Status = r.Status.ToString().ToLowerInvariant(),
                r.StartedAt,
                FinishedAt = (DateTimeOffset?)null,
                r.QueuePosition,
                CanCancel = workflows[r.WorkflowId].Settings.Portal?.AllowCancel ?? true
            });
        var finished = executions
            .Where(e => workflows.ContainsKey(e.WorkflowId))
            .Select(e => new
            {
                ExecutionId = e.Id,
                e.WorkflowId,
                e.WorkflowName,
                Status = e.Status.ToString().ToLowerInvariant(),
                e.StartedAt,
                FinishedAt = e.FinishedAt,
                QueuePosition = (int?)null,
                CanCancel = false
            });
        return PipeApiEndpoints.Json(active.Concat(finished).OrderByDescending(r => r.StartedAt).ToList());
    }

    static private async Task<IResult> GetExecutionAsync(string workflowId, string id, HttpContext context)
    {
        if (User(context) is not { } user)
        {
            return NoUser();
        }
        // another user's run, or one of a workflow outside of the key, is not there
        if (await PipeApiEndpoints.Services<IPipeWorkflowStore>(context).GetAsync(workflowId, context.RequestAborted) is not { } workflow
            || !PipeApiEndpoints.Key(context).Covers(workflow.GroupId)
            || !await OwnsAsync(user, workflowId, id, context))
        {
            return PipeApiEndpoints.NotFound($"Execution '{id}'");
        }

        var running = PipeApiEndpoints.Services<IPipeRunningExecutions>(context);
        var execution = running.GetFinished(id)
                        ?? await PipeApiEndpoints.Services<IPipeExecutionStore>(context).GetAsync(workflowId, id, context.RequestAborted);
        if (execution is not null)
        {
            return PipeApiEndpoints.Json(View(workflow, execution, context));
        }
        return running.Find(id) is { } info && info.WorkflowId == workflowId
            ? PipeApiEndpoints.Json(new
            {
                ExecutionId = id,
                WorkflowId = workflowId,
                info.WorkflowName,
                Status = info.Status.ToString().ToLowerInvariant(),
                info.StartedAt,
                info.QueuePosition,
                CanCancel = workflow.Settings.Portal?.AllowCancel ?? true
            })
            : PipeApiEndpoints.NotFound($"Execution '{id}'");
    }

    static private async Task<IResult> CancelAsync(string workflowId, string id, HttpContext context)
    {
        if (User(context) is not { } user)
        {
            return NoUser();
        }
        if (await PipeApiEndpoints.Services<IPipeWorkflowStore>(context).GetAsync(workflowId, context.RequestAborted) is not { } workflow
            || !PipeApiEndpoints.Key(context).Covers(workflow.GroupId)
            || !await OwnsAsync(user, workflowId, id, context))
        {
            return PipeApiEndpoints.NotFound($"Execution '{id}'");
        }
        if (workflow.Settings.Portal is { AllowCancel: false })
        {
            return PipeApiEndpoints.Problem(StatusCodes.Status403Forbidden, "This workflow can't be stopped by users");
        }

        var running = PipeApiEndpoints.Services<IPipeRunningExecutions>(context);
        if (running.Find(id) is not { WorkflowId: var owner } || owner != workflowId || !running.Cancel(id))
        {
            return PipeApiEndpoints.Problem(StatusCodes.Status409Conflict, $"Execution '{id}' isn't running");
        }
        await PipeApiEndpoints.WriteAuditAsync(context, PipeAuditArea.Execution, "stopped", workflowId, workflow.Name, $"execution {id} by {UserPrefix}{user}");
        return Results.Accepted();
    }
}
