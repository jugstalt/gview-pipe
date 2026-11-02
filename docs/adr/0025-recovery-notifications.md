# 25. Recovery notifications

Date: 2026-11-02 · Status: accepted

## Context

The error workflow ([ADR 0017](0017-failure-notifications.md)) reports a failure, but nobody learns that the workflow runs again. People keep watching an alarm that is long over.

## Decision

### Open failure of a workflow

- A workflow with a failed triggered run has an *open failure* (`PipeWorkflowHealth`: `failingSince`, `failedExecutionId`, `failures`, `notified`), kept by `IPipeWorkflowHealthStore`: one file per workflow in `data/health/<id>.json`, in memory without the file store.
- It is written when a run in mode `Trigger`, `Replay` or `Api` fails and an error workflow with an Error Trigger applies (the same conditions as ADR 0017). `failures` counts every failed run of the streak, `notified` becomes true when the error workflow actually ran for one of them (not suppressed by the throttle).
- Manual runs and runs as sub-workflow don't change it; the error workflow itself is never tracked.
- A successful run in mode `Trigger`, `Replay` or `Api` ends it: the file is deleted. Deleting the workflow deletes the file too.

### Recovery Trigger

- A new entry node *Recovery Trigger* (`IPipeRecoveryTrigger`) in the effective error workflow (workflow → group → instance). The Error Trigger is unchanged: an error workflow without a Recovery Trigger reports nothing, existing ones behave as before.
- When the open failure ends and `notified` is true, the Recovery Trigger starts (mode `Error`) with `{ workflow: { id, name }, execution: { id, mode, environment, startedAt, url? }, failedSince, failedExecution: { id, url? }, failures }`.
- A failure that was never reported (throttled only, or no error workflow) is never reported as recovered.
- Problems are only logged, like in ADR 0017.

### Samples

- *Error Mail*, *Error Teams* and *Error Slack* got a second, disabled branch from a Recovery Trigger ("… is running again").

## Consequences

- A workflow that fails and recovers between two runs of the error workflow's throttle window sends both messages; the recovery isn't throttled.
- The state survives a restart, so a failure before a restart still ends with a message. The throttle counter of ADR 0017 stays in memory.
- A "failing since …" hint in the workflow list would use the same data; it is not part of this.
