# 32. Metrics with OpenTelemetry

Date: 2026-11-03 · Status: accepted

## Context

Runs, duration and errors are visible in the execution history, but not to monitoring tools. The ServiceDefaults of the AppHost already configure OpenTelemetry for ASP.NET Core, HTTP and the runtime.

## Decision

- `PipeMetrics` (Core) owns the meter `gView.Pipe` (`System.Diagnostics.Metrics`, no package needed in Core). Hosts switch the metrics on by adding the meter (`AddMeter("gView.Pipe")`); the ServiceDefaults do.
- Instruments:

| Name | Type | Tags |
|---|---|---|
| `gview.pipe.runs` | counter | `workflow.id`, `workflow.name`, `status` (success, error, canceled), `mode` |
| `gview.pipe.run.duration` | histogram, seconds | the same |
| `gview.pipe.runs.skipped` | counter | `workflow.id`, `workflow.name` |
| `gview.pipe.runs.active` | up-down counter | none |

- `PipeWorkflowRunner` records a run when the engine returns, so all modes (manual, trigger, API, replay, sub-workflow, error workflow) count. Skipped runs are those dropped because the workflow was busy and its queue full ([ADR 0016](0016-queued-runs.md)).
- Errors per workflow are the runs with `status=error`.

## Consequences

- The number of workflows bounds the cardinality of the tags.
- Without an exporter (`OTEL_EXPORTER_OTLP_ENDPOINT`) nothing leaves the process.
