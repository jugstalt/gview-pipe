namespace gView.Pipe.Abstraction.Nodes;

/// <summary>
/// The logic behind a node. A new instance is created for every execution
/// (constructor injection is supported), so implementations must not keep state between runs.
/// </summary>
public interface IPipeNode
{
    /// <summary>Processes all input items at once and returns the output items per port.</summary>
    Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken);
}

/// <summary>
/// A node whose parameters are described by <typeparamref name="TParameters"/>.
/// The UI generates the parameter form from this type.
/// </summary>
public interface IPipeNode<TParameters> : IPipeNode
    where TParameters : class, new()
{
}
