using gView.Pipe.Abstraction.Data;

namespace gView.Pipe.Abstraction.Nodes;

/// <summary>
/// Base class for the common 1:1 case: every item of the main input is processed on its own,
/// with parameters evaluated against that item. Return <c>null</c> to drop an item.
/// </summary>
public abstract class PipeItemNodeBase<TParameters> : IPipeNode<TParameters>
    where TParameters : class, new()
{
    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput(PipePorts.Main);
        var output = new List<PipeItem>(items.Count);

        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parameters = context.GetParameters<TParameters>(items[i], i);
            var result = await ProcessItemAsync(items[i], parameters, context, cancellationToken);
            if (result is not null)
            {
                output.Add(result);
            }
        }

        return PipeNodeResult.FromMain(output);
    }

    protected abstract Task<PipeItem?> ProcessItemAsync(
        PipeItem item,
        TParameters parameters,
        IPipeNodeContext context,
        CancellationToken cancellationToken);
}
