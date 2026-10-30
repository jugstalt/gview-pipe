using gView.Pipe.Abstraction.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace gView.Pipe.Plugins.Nodes.Flow;

public enum WaitUnit
{
    Seconds,
    Minutes,
    Hours
}

public sealed class WaitNodeParameters
{
    [PipeParameter(Label = "Wait for", Order = 1)]
    [Range(0, double.MaxValue)]
    public double Amount { get; set; } = 5;

    [PipeParameter(Label = "Unit", Expressions = false, Order = 2)]
    public WaitUnit Unit { get; set; } = WaitUnit.Seconds;
}

/// <summary>Host settings of the Wait node: <c>services.Configure&lt;PipeWaitOptions&gt;(…)</c>.</summary>
public sealed class PipeWaitOptions
{
    /// <summary>
    /// Longest allowed wait. Waiting happens in memory: an execution that waits is lost when the host restarts,
    /// so long waits should be schedules instead.
    /// </summary>
    public TimeSpan MaxWait { get; set; } = TimeSpan.FromHours(1);
}

[PipeNode(PipeBuiltInNodes.Wait, "Wait",
    Category = "Flow",
    Description = "Pauses the execution, then passes the items on. Waits in memory; a restart of the host ends the execution.")]
public sealed class WaitNode : IPipeNode<WaitNodeParameters>
{
    private readonly PipeWaitOptions _options;

    public WaitNode(IOptions<PipeWaitOptions> options)
    {
        _options = options.Value;
    }

    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var parameters = context.GetParameters<WaitNodeParameters>();
        var duration = parameters.Unit switch
        {
            WaitUnit.Hours => TimeSpan.FromHours(parameters.Amount),
            WaitUnit.Minutes => TimeSpan.FromMinutes(parameters.Amount),
            _ => TimeSpan.FromSeconds(parameters.Amount)
        };

        if (duration < TimeSpan.Zero)
        {
            throw new InvalidOperationException("The wait time can't be negative");
        }
        if (duration > _options.MaxWait)
        {
            throw new InvalidOperationException($"Waiting {duration} exceeds the allowed maximum of {_options.MaxWait}");
        }

        context.Logger.LogInformation("Waiting {Duration}", duration);
        await Task.Delay(duration, cancellationToken);

        return PipeNodeResult.FromMain(context.GetInput());
    }
}
