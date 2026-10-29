using gView.Pipe.Abstraction.Workflows;

namespace gView.Pipe.Abstraction.Triggers;

/// <summary>Arms and disarms the triggers of workflows.</summary>
public interface IPipeTriggerManager
{
    /// <summary>(Re)arms the triggers of <paramref name="workflow"/> if it is active, disarms them otherwise.</summary>
    Task ActivateAsync(PipeWorkflow workflow, CancellationToken cancellationToken = default);

    Task DeactivateAsync(string workflowId);

    bool IsActive(string workflowId);
}
