# 15. Execution history and replay

Date: 2026-11-01 · Status: accepted

## Context

Every execution is stored (`IPipeExecutionStore`), the last 50 per workflow, and the editor lists the last 20 of the open workflow. Nodes have `RetryCount`, `RetryDelayMs` and `TimeoutSeconds` (see [ADR 0005](0005-execution-semantics.md)). Missing are a view of the runs of all workflows, a way to run a failed execution again after fixing the workflow, a retry delay that grows, and a retention that can be configured.

## Decision

### Retry backoff

- Nodes get `RetryBackoff` (`fixed`, the default, or `exponential`) and `RetryMaxDelayMs` (0 = no limit). Exponential doubles the delay after each failed attempt: `RetryDelayMs · 2^(attempt-1)`, capped by `RetryMaxDelayMs`.
- Retries stay unconditional. Retrying only on certain errors (HTTP 5xx, timeout) is the node's business, not the engine's.

### Retention

- `Pipe:ExecutionRetention` (count, default 50, as before) and `Pipe:ExecutionRetentionDays` (age, default 0 = no limit) in `pipe.config`.
- Workflows can override both in their settings (`ExecutionRetention`, `ExecutionRetentionDays`; empty = the instance value).
- The runner prunes the workflow after each stored execution. A hosted service prunes all workflows once a day, so the age limit also applies to workflows that no longer run.

### Index

- The history page must list executions of all workflows without reading every execution file. The file store writes a small summary next to each execution (`{startedAt}_{id}.summary.json`): workflow, status, mode, environment, start, end, error, error node, replay origin, and whether data was saved.
- `IPipeExecutionStore` gets `QueryAsync(PipeExecutionQuery)`, which returns summaries (`PipeExecutionSummary`), newest first, filtered by workflows, status, mode and time range, paged with a `before` timestamp. Executions without a summary file (written before this change) are skipped by the query but still opened by `GetAsync`.
- No database store; it stays an idea of its own.

### Replay

- A replay always runs the **current** workflow. Snapshots of the workflow at run time belong to workflow versioning, which comes later.
- Replay modes:
  - **From the start:** the stored outputs of the start nodes (the trigger) are used as their output.
  - **From node X:** X and everything downstream of it is executed again; every other node that takes part returns its stored outputs, as pin data does in a manual run. The stored outputs are used for this run only and are not saved in the workflow.
  - **From the failed node:** "from node X" with the execution's `ErrorNodeId`.
- A node that has no stored run (added after the original execution) and is needed before X is executed normally.
- New execution mode `Replay`. It behaves like `Trigger`: workflow pin data is ignored and the error workflow starts on failure. It runs in the **environment of the original execution** and records `ReplayOfExecutionId`.
- Executions stored without data (`SaveExecutionData = false`) cannot be replayed; the UI says why. Binaries are not kept after an execution, so stored items may reference binaries that are gone: the replay logs a warning, and a node fails only when it reads such a binary.
- Executions of sub-workflows can be replayed on their own: the replay is a top-level run (`Depth 0`, no parent).
- Several executions can be replayed at once. They run one after the other, ordered by their original start, and wait for a free slot instead of being skipped by `MaxConcurrentRuns`.
- Every signed-in user may replay (finer roles come later).

### UI

- A history page `/executions` (component `PipeExecutionList` in `gView.Pipe.Razor`, linked in the navigation) lists the executions of all workflows: workflow, status, mode, environment, start, duration, failed node. Filters: status, workflow or group, mode, time range. More rows load on demand. A row opens the execution in the editor (`/workflows/{id}?execution={executionId}`).
- Replay is offered on the history page (one or several executions) and in the editor for an opened execution: next to the result chip (from the start, from the failed node), and "replay from here" in the toolbar of the selected node, next to "run to here" (nodes have no context menu).
- A replay from the editor runs the saved workflow; with unsaved changes the editor asks to save first.
- Replays from the history page run in the background, so leaving the page doesn't stop them.

## Consequences

- Each execution writes one more small file. Old executions without a summary don't appear on the history page until they are pruned.
- The engine gets replay data in `PipeExecutionRequest`; the pin data logic is shared by both.
- Waiting for a slot is new for the runner; triggered runs keep being skipped.
- The work is done in two steps: backoff, retention and index first; history page and replay second.
