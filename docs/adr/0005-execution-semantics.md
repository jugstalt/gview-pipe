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
- **Errors:** each node has `RetryCount`, `RetryDelayMs` and `TimeoutSeconds`, plus one of three `OnError` modes:
  - `Stop` (default): fails the execution and cancels running nodes.
  - `Continue`: passes the input on.
  - `ErrorOutput`: routes the input items to the `error` port, each with an `error` field.
- **Versioning:** nodes are stored with `typeVersion`. When it is lower than the node type's version, `IPipeNodeMigration` upgrades the parameters before execution.
- **Expressions:**
  - The MVP uses path templating only (`IPipeExpressionEvaluator`), with no operators or functions. Comparisons are done by If, Filter and Switch.
  - A template that is a single expression keeps its JSON type. Mixed text becomes a string.
- **Concurrency and history:**
  - `MaxConcurrentRuns` applies per workflow (default 1). Surplus runs are skipped.
  - The last 50 executions per workflow are kept. With `SaveExecutionData = false`, only statuses and counts are stored.
- **Schedules:** cron triggers use Cronos (5 or 6 fields) with a timer loop per registration.

## Consequences

- An execution's data lives in memory until it finishes. Very large item counts will need streaming or batching later.
