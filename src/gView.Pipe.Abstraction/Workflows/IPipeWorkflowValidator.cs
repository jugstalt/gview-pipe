namespace gView.Pipe.Abstraction.Workflows;

/// <param name="Errors">Problems that keep the workflow from being saved, e.g. a cycle or an unknown node type.</param>
/// <param name="Warnings">Things to look at that don't stop it, e.g. a credential that doesn't exist here.</param>
public sealed record PipeWorkflowValidation(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Checks a workflow before it is saved from outside the editor, e.g. through the REST API (ADR 0020).</summary>
public interface IPipeWorkflowValidator
{
    Task<PipeWorkflowValidation> ValidateAsync(PipeWorkflow workflow, CancellationToken cancellationToken = default);
}
