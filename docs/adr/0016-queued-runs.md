# 16. Queued runs

Date: 2026-11-02 · Status: accepted

## Context

`MaxConcurrentRuns` limits the runs of a workflow (default 1). Runs beyond it are skipped: a webhook caller gets `429`, a cron tick is lost. Bursts of webhook calls or a cron run overlapping with a manual one lose work silently. Replays already wait for a free slot (see [ADR 0015](0015-execution-history-and-replay.md)).

## Decision

- Workflows get `MaxQueuedRuns` (default 0 = skip as before). Above 0, that many runs wait for a free slot; further ones are skipped.
- The queue applies to runs in mode `Trigger` (cron, webhook) and `Error`. Manual runs from the editor and sub-workflow calls are never queued: the editor wants an answer now, and a sub-workflow already runs inside its caller.
- Waiting runs start per workflow in the order they arrived (FIFO).
- The queue lives in memory. A restart drops waiting runs; they are logged.
- Replays keep waiting without limit and don't count against `MaxQueuedRuns`.
- Cron needs no rule of its own: the scheduler waits for the run of its tick, so a cron trigger never has more than one run waiting.

### Queue ticket

- A trigger can pass a `PipeQueueTicket` (`PipeTriggerFireOptions.QueueTicket`). The runner marks it `Queued`, `Started` or `Rejected`; the trigger can `TryAbandon` it while it still waits. Start and abandon are decided atomically, so a run never starts after its caller was told it didn't.

### Webhooks

- The webhook's `TimeoutSeconds` counts from the arrival of the call, waiting included.
  - Timeout while the run still waits: `503` with `Retry-After`, and the run leaves the queue.
  - Timeout while the run runs: `202 { status: "running" }` as before.
- Response mode "immediately" answers `202 { status: "queued" }` when the run waits, `202 { status: "started" }` when it runs.
- Queue full (or no queue): `429` as before.

### Display

- Waiting runs are listed in "Running now" with status `Queued` and their position, and can be canceled there. They count as running for the "n running" indicator.
- A run canceled or abandoned while waiting never started, so it has no execution in the history; the log records it.

## Consequences

- `PipeExecutionStatus` gets `Queued`, used only for live views; stored executions never have it.
- The execution id is assigned before the run waits, so it can be shown and canceled while queued.
