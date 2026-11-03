using gView.Pipe.Abstraction.Execution;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace gView.Pipe.Core.Engine;

/// <summary>
/// OpenTelemetry metrics of the engine (ADR 0032). The meter is <see cref="MeterName"/>; hosts add it with
/// <c>AddMeter</c>, the ServiceDefaults of the AppHost do.
/// </summary>
static public class PipeMetrics
{
    public const string MeterName = "gView.Pipe";

    static private readonly Meter Meter = new(MeterName);

    /// <summary>Finished runs, tagged with workflow, status and mode.</summary>
    static private readonly Counter<long> Runs = Meter.CreateCounter<long>("gview.pipe.runs", "{run}", "Finished workflow runs");

    /// <summary>Duration of finished runs in seconds.</summary>
    static private readonly Histogram<double> Duration = Meter.CreateHistogram<double>("gview.pipe.run.duration", "s", "Duration of workflow runs");

    /// <summary>Triggered runs that were dropped because the workflow was busy and its queue full.</summary>
    static private readonly Counter<long> Skipped = Meter.CreateCounter<long>("gview.pipe.runs.skipped", "{run}", "Runs skipped because the workflow was busy");

    static private long _running;

    static PipeMetrics()
    {
        Meter.CreateObservableUpDownCounter("gview.pipe.runs.active", () => Interlocked.Read(ref _running), "{run}", "Runs in progress");
    }

    static internal void RunStarted() => Interlocked.Increment(ref _running);

    static internal void RunAborted() => Interlocked.Decrement(ref _running);

    static internal void RunFinished(PipeExecution execution)
    {
        Interlocked.Decrement(ref _running);

        var tags = new TagList
        {
            { "workflow.id", execution.WorkflowId },
            { "workflow.name", execution.WorkflowName },
            { "status", ToTag(execution.Status) },
            { "mode", execution.Mode.ToString().ToLowerInvariant() }
        };
        Runs.Add(1, tags);

        if (execution.FinishedAt is { } finished)
        {
            Duration.Record((finished - execution.StartedAt).TotalSeconds, tags);
        }
    }

    static internal void RunSkipped(string workflowId, string workflowName)
        => Skipped.Add(1, new TagList { { "workflow.id", workflowId }, { "workflow.name", workflowName } });

    static private string ToTag(PipeExecutionStatus status) => status.ToString().ToLowerInvariant();
}
