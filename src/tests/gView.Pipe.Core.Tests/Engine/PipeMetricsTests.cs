using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Engine;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.Metrics;

namespace gView.Pipe.Core.Tests.Engine;

public sealed class PipeMetricsTests
{
    [Fact]
    public async Task A_finished_run_is_counted_with_its_workflow_and_status()
    {
        var runs = new List<(long Value, string? Workflow, string? Status)>();
        var durations = new List<double>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == PipeMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "gview.pipe.runs")
            {
                lock (runs)
                {
                    runs.Add((value, Tag(tags, "workflow.id"), Tag(tags, "status")));
                }
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
        {
            if (instrument.Name == "gview.pipe.run.duration")
            {
                lock (durations)
                {
                    durations.Add(value);
                }
            }
        });
        listener.Start();

        using var host = PipeTestHost.Create();
        var runner = host.GetRequiredService<IPipeWorkflowRunner>();
        var execution = await runner.RunAsync(new PipeWorkflow { Id = "metrics-w", Name = "Metrics" });

        Assert.NotNull(execution);
        lock (runs)
        {
            Assert.Contains(runs, r => r.Value == 1 && r.Workflow == "metrics-w" && r.Status == execution.Status.ToString().ToLowerInvariant());
        }
        lock (durations)
        {
            Assert.NotEmpty(durations);
        }
    }

    static private string? Tag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == key)
            {
                return tag.Value?.ToString();
            }
        }
        return null;
    }
}
