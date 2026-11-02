using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Options;
using Microsoft.Extensions.Options;

namespace gView.Pipe.Core.Workflows;

internal sealed class PipeErrorWorkflowService : IPipeErrorWorkflows
{
    public PipeErrorWorkflowService(IOptions<PipeCoreOptions> options)
    {
        Instance = string.IsNullOrWhiteSpace(options.Value.ErrorWorkflowId) ? null : options.Value.ErrorWorkflowId.Trim();
    }

    public string? Instance { get; }

    public PipeEffectiveErrorWorkflow Resolve(PipeWorkflow workflow, PipeWorkflowGroup? group)
    {
        var effective = Pick(workflow.Settings.ErrorWorkflowId, PipeErrorWorkflowSource.Workflow, null)
            ?? (group is not null && group.Id == workflow.GroupId ? Pick(group.ErrorWorkflowId, PipeErrorWorkflowSource.Group, group.Name) : null)
            ?? Pick(Instance, PipeErrorWorkflowSource.Instance, null)
            ?? new PipeEffectiveErrorWorkflow(null, PipeErrorWorkflowSource.None);

        // an error workflow is never started for its own failures
        return effective.WorkflowId == workflow.Id ? new PipeEffectiveErrorWorkflow(null, PipeErrorWorkflowSource.None) : effective;
    }

    /// <summary>Null when the level doesn't set one (inherit); "none" ends the search without a workflow.</summary>
    static private PipeEffectiveErrorWorkflow? Pick(string? id, PipeErrorWorkflowSource source, string? sourceName) => id switch
    {
        null or "" => null,
        PipeErrorWorkflows.None => new PipeEffectiveErrorWorkflow(null, PipeErrorWorkflowSource.None, sourceName),
        _ => new PipeEffectiveErrorWorkflow(id, source, sourceName)
    };
}
