using gView.Pipe.Abstraction.Nodes;
using System.ComponentModel.DataAnnotations;

namespace gView.Pipe.Plugins.Nodes.Transform;

public enum LimitKeep
{
    FirstItems,
    LastItems
}

public sealed class LimitNodeParameters
{
    [PipeParameter(Label = "Max items", Expressions = false)]
    [Range(0, int.MaxValue)]
    public int MaxItems { get; set; } = 1;

    [PipeParameter(Label = "Keep", Expressions = false)]
    public LimitKeep Keep { get; set; } = LimitKeep.FirstItems;
}

[PipeNode(PipeBuiltInNodes.Limit, "Limit",
    Category = "Transform",
    Description = "Keeps only the first or the last N items.")]
public sealed class LimitNode : IPipeNode<LimitNodeParameters>
{
    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var parameters = context.GetParameters<LimitNodeParameters>();
        var items = context.GetInput();
        var count = Math.Max(0, parameters.MaxItems);

        var kept = parameters.Keep == LimitKeep.LastItems ? items.TakeLast(count) : items.Take(count);
        return Task.FromResult(PipeNodeResult.FromMain(kept.ToList()));
    }
}
