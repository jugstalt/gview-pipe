using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;

namespace gView.Pipe.Plugins.Nodes.Flow;

public sealed class FilterNodeParameters : PipeConditionParameters
{
}

[PipeNode(PipeBuiltInNodes.Filter, "Filter",
    Category = "Flow",
    Description = "Keeps only the items that match a condition.")]
public sealed class FilterNode : PipeItemNodeBase<FilterNodeParameters>
{
    protected override Task<PipeItem?> ProcessItemAsync(PipeItem item, FilterNodeParameters parameters, IPipeNodeContext context, CancellationToken cancellationToken)
        => Task.FromResult(PipeConditions.Evaluate(parameters) ? item : null);
}
