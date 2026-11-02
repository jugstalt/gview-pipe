namespace gView.Pipe.Abstraction.Triggers;

/// <summary>Entry point of a workflow that other workflows call (Execute Workflow). Receives the caller's items.</summary>
public interface IPipeSubWorkflowTrigger : IPipeTriggerNode
{
}

/// <summary>
/// Entry point of an error workflow. Receives one item describing the failed execution:
/// <c>{ workflow: { id, name }, execution: { id, mode, environment, startedAt, error, url? }, node: { id, name, error },
/// subWorkflow?: { id, name, executionId, error, node }, suppressed }</c> (see ADR 0017).
/// </summary>
public interface IPipeErrorTrigger : IPipeTriggerNode
{
}

/// <summary>
/// Entry point of a workflow that reports a recovery: a workflow that had failed (and whose failure was reported to the
/// error workflow) ran successfully again. Receives <c>{ workflow: { id, name }, execution: { id, mode, environment, startedAt, url? },
/// failedSince, failedExecution: { id, url? }, failures }</c> (see ADR 0025). Found in the effective error workflow.
/// </summary>
public interface IPipeRecoveryTrigger : IPipeTriggerNode
{
}
