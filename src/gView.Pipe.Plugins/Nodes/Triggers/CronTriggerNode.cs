using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Triggers;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Triggers;

public sealed class CronTriggerNodeParameters
{
    [Required, PipeParameter(Label = "Cron expression", Placeholder = "0 * * * *",
        Description = "5 fields (minute hour day month weekday) or 6 with seconds first", Expressions = false)]
    public string Expression { get; set; } = "0 * * * *";

    [PipeParameter(Label = "Time zone", Placeholder = "UTC", Description = "IANA or Windows time zone id", Expressions = false)]
    public string TimeZone { get; set; } = "UTC";
}

[PipeNode(PipeBuiltInNodes.CronTrigger, "Cron Trigger",
    Category = "Triggers",
    Description = "Starts the workflow on a schedule while the workflow is active.")]
public sealed class CronTriggerNode : IPipeTriggerNode, IPipeNode<CronTriggerNodeParameters>
{
    public Task<IAsyncDisposable?> ActivateAsync(IPipeTriggerContext context, CancellationToken cancellationToken)
    {
        var parameters = context.GetParameters<CronTriggerNodeParameters>();
        var timeZone = string.IsNullOrWhiteSpace(parameters.TimeZone) || parameters.TimeZone.Equals("UTC", StringComparison.OrdinalIgnoreCase)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.FindSystemTimeZoneById(parameters.TimeZone);

        var registration = context.Host.RegisterSchedule(parameters.Expression, timeZone, ct =>
        {
            var item = new PipeItem(new JsonObject
            {
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
                ["expression"] = parameters.Expression
            });
            return context.FireAsync([item], ct);
        });

        return Task.FromResult<IAsyncDisposable?>(new Registration(registration));
    }

    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
        => Task.FromResult(PipeNodeResult.FromMain(context.GetInput()));

    private sealed class Registration : IAsyncDisposable
    {
        private readonly IDisposable _registration;

        public Registration(IDisposable registration)
        {
            _registration = registration;
        }

        public ValueTask DisposeAsync()
        {
            _registration.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
