# 5. Execution semantics

Date: 2026-10-29 · Status: accepted

## Decision

- **Graph:** only DAGs are allowed, and cycles are rejected. Loops will come later as a dedicated node.
- **Start nodes:** a trigger run starts at the trigger that fired. A manual run starts at every node without incoming connections. Only nodes reachable from the start nodes take part.
- **Scheduling:**
  - A node starts once all of its *reachable* predecessors have finished. Independent branches run in parallel.
  - All items from all incoming connections are concatenated per target port.
- **Empty input:** a node is skipped when all of its inputs are empty. Start nodes and nodes with `AlwaysExecute` still run. Skipping propagates downstream.
- **Disabled nodes:** they pass all input items through on `main`.
- **Pin data:** in manual runs, a node with pin data (`PipeWorkflow.PinData`, items per output port) is not executed; it returns the pinned items, and its run is marked `Pinned`. Triggered runs ignore pin data, so production runs never work with test data. Pinned items carry no binaries, because binary content belongs to the execution it came from.
- **Run to here:** with `PipeExecutionRequest.TargetNodeId`, only the target and the nodes it depends on take part. Start nodes are the roots it can be reached from.
- **Sub-workflows:** the Execute Workflow node runs another workflow through `IPipeNodeContext.ExecuteWorkflowAsync`: once for all items, per item or per batch, one call after the other. The sub-workflow starts at its Execute Workflow Trigger (`IPipeSubWorkflowTrigger`), runs through the runner (history, concurrency limit) in mode `SubWorkflow` with `ParentExecutionId` and `Depth`, and shares the caller's binary store, so binaries go in and come back. The output is the items of its last finished node. A failed sub-workflow fails the calling node, where `OnError` applies. Nesting is limited by `PipeCoreOptions.MaxSubWorkflowDepth` (10).
- **Error workflows:** when a run in mode `Trigger` or `SubWorkflow` fails, the runner starts the workflow in `PipeWorkflowSettings.ErrorWorkflowId` at its Error Trigger (`IPipeErrorTrigger`), with one item `{ workflow, execution, node }`. Manual runs don't; failures of the error workflow itself are only logged.
- **Loops and waiting:** there are no cycles. Repetition is done by calling a sub-workflow per item or batch. The Wait node pauses in memory, up to `PipeWaitOptions.MaxWait` (default one hour); a host restart ends a waiting execution. Resumable waits need persisted executions and come later.
- **Errors:** each node has `RetryCount`, `RetryDelayMs` and `TimeoutSeconds`, plus one of three `OnError` modes:
  - `Stop` (default): fails the execution and cancels running nodes.
  - `Continue`: passes the input on.
  - `ErrorOutput`: routes the input items to the `error` port, each with an `error` field.
- **Versioning:** nodes are stored with `typeVersion`. When it is lower than the node type's version, `IPipeNodeMigration` upgrades the parameters before execution.
- **Expressions:**
  - Expressions are JavaScript (`IPipeExpressionEvaluator`, see [ADR 0007](0007-javascript-expressions.md)). The MVP's path-only templating is replaced.
  - A template that is a single expression keeps its JSON type. Mixed text becomes a string.
- **Node runs:** each node run records:
  - a snapshot of its input items (nodes may change what they received)
  - its output items
  - its log entries. The node's `ILogger` writes to the application log and to the run, and every entry also goes to the observers right away (`IPipeExecutionObserver.OnNodeLogAsync`), so the editor can show a node's output live while it runs (e.g. a script's progress). The engine adds its messages about the node (retries, failure, cancel). The log is capped at 1,000 entries per run.
- **Concurrency and history:**
  - `MaxConcurrentRuns` applies per workflow (default 1). Surplus runs are skipped.
  - The last 50 executions per workflow are kept. With `SaveExecutionData = false`, only statuses, counts and logs are stored, without input or output items.
- **Schedules:** cron triggers use Cronos (5 or 6 fields) with a timer loop per registration.

## Consequences

- An execution's data lives in memory until it finishes. Very large item counts will need streaming or batching later.
