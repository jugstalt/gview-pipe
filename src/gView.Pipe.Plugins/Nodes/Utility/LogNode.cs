using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Common;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Utility;

/// <summary>The levels of <see cref="LogLevel"/>, with the same names and values, so stored workflows keep working.</summary>
public enum LogNodeLevel
{
    [PipeEnumValue("Very detailed, usually switched off")]
    Trace = LogLevel.Trace,

    [PipeEnumValue("Details for finding errors")]
    Debug = LogLevel.Debug,

    [PipeEnumValue("Normal messages")]
    Information = LogLevel.Information,

    [PipeEnumValue("Something unexpected that didn't stop the run")]
    Warning = LogLevel.Warning,

    [PipeEnumValue("Something failed")]
    Error = LogLevel.Error,

    [PipeEnumValue("Something failed badly")]
    Critical = LogLevel.Critical,

    [PipeEnumValue("Write nothing; the items just pass")]
    None = LogLevel.None
}

public sealed class LogNodeParameters
{
    [PipeParameter(Label = "Message", Placeholder = "{{ $json }}", Description = "What to write per item, e.g. Order {{ $json.id }} received; {{ $json }} writes the whole item")]
    public JsonNode? Message { get; set; } = "{{ $json }}";

    [PipeParameter(Label = "Level", Expressions = false, Description = "How important the message is; the host decides which levels are kept")]
    public LogNodeLevel Level { get; set; } = LogNodeLevel.Information;
}

[PipeNodeExamples]
[PipeNode(PipeBuiltInNodes.Log, "Log",
    Category = "Utility",
    Description = "Writes a message per item to the application log and passes the items on.")]
public sealed class LogNode : PipeItemNodeBase<LogNodeParameters>
{
    protected override Task<PipeItem?> ProcessItemAsync(PipeItem item, LogNodeParameters parameters, IPipeNodeContext context, CancellationToken cancellationToken)
    {
        context.Logger.Log((LogLevel)parameters.Level, "[{Workflow}/{Node}] {Message}",
            context.WorkflowName, context.NodeName, PipeConditions.ToText(parameters.Message));

        return Task.FromResult<PipeItem?>(item);
    }
}
