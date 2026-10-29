using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;

namespace gView.Pipe.Plugins.Nodes.Flow;

public sealed class IfNodeParameters : PipeConditionParameters
{
}

[PipeNode(PipeBuiltInNodes.If, "If",
    Category = "Flow",
    Description = "Routes each item to 'true' or 'false' depending on a condition.",
    Outputs = new[] { TruePort, FalsePort })]
public sealed class IfNode : IPipeNode<IfNodeParameters>
{
    public const string TruePort = "true";
    public const string FalsePort = "false";

    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput();
        var matches = new List<PipeItem>();
        var others = new List<PipeItem>();

        for (var i = 0; i < items.Count; i++)
        {
            var parameters = context.GetParameters<IfNodeParameters>(items[i], i);
            (PipeConditions.Evaluate(parameters) ? matches : others).Add(items[i]);
        }

        return Task.FromResult(PipeNodeResult.FromPorts((TruePort, matches), (FalsePort, others)));
    }
}
