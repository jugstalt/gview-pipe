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
            if (i > 0 && DelayBetweenItems(parameters) is { Ticks: > 0 } delay)
            {
                // ends at once when the run is stopped
                await Task.Delay(delay, cancellationToken);
            }

            var result = await ProcessItemAsync(items[i], parameters, context, cancellationToken);
            if (result is not null)
            {
                output.Add(result);
            }
        }

        return PipeNodeResult.FromMain(output);
    }

    /// <summary>How long to wait before an item that isn't the first, e.g. for the rate limit of an API. None by default.</summary>
    protected virtual TimeSpan DelayBetweenItems(TParameters parameters) => TimeSpan.Zero;

    protected abstract Task<PipeItem?> ProcessItemAsync(
        PipeItem item,
        TParameters parameters,
        IPipeNodeContext context,
        CancellationToken cancellationToken);
}
