namespace gView.Pipe.Abstraction.Triggers;

/// <summary>Entry point of a workflow that other workflows call (Execute Workflow). Receives the caller's items.</summary>
public interface IPipeSubWorkflowTrigger : IPipeTriggerNode
{
}

/// <summary>
/// Entry point of an error workflow. Receives one item describing the failed execution:
/// <c>{ workflow: { id, name }, execution: { id, mode, startedAt, error }, node: { id, name } }</c>.
/// </summary>
public interface IPipeErrorTrigger : IPipeTriggerNode
{
}
