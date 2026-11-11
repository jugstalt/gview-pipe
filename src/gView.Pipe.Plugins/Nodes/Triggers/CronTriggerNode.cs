using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Triggers;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Triggers;

public sealed class CronTriggerNodeParameters
{
    [Required, PipeParameter(Label = "Cron expression", Placeholder = "0 * * * *",
        Description = "When to run: minute hour day month weekday, e.g. 0 7 * * 1-5 is 7:00 on workdays, */15 * * * * every 15 minutes", Expressions = false)]
    public string Expression { get; set; } = "0 * * * *";

    [PipeParameter(Label = "Time zone", Placeholder = "UTC", Description = "Time zone of the schedule, e.g. Europe/Vienna", Expressions = false)]
    public string TimeZone { get; set; } = "UTC";
}

[PipeNodeExamples]
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
