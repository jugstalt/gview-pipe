using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Utility;

public sealed class LogNodeParameters
{
    [PipeParameter(Label = "Message", Placeholder = "{{ $json }}")]
    public JsonNode? Message { get; set; } = "{{ $json }}";

    [PipeParameter(Label = "Level", Expressions = false)]
    public LogLevel Level { get; set; } = LogLevel.Information;
}

[PipeNode(PipeBuiltInNodes.Log, "Log",
    Category = "Utility",
    Description = "Writes a message per item to the application log and passes the items on.")]
public sealed class LogNode : PipeItemNodeBase<LogNodeParameters>
{
    protected override Task<PipeItem?> ProcessItemAsync(PipeItem item, LogNodeParameters parameters, IPipeNodeContext context, CancellationToken cancellationToken)
    {
        context.Logger.Log(parameters.Level, "[{Workflow}/{Node}] {Message}",
            context.WorkflowName, context.NodeName, PipeConditions.ToText(parameters.Message));

        return Task.FromResult<PipeItem?>(item);
    }
}
