# 34. Split in Batches and pauses between calls

Date: 2026-11-03 · Status: accepted

## Context

Loops are done by calling a sub-workflow per item or batch ([ADR 0005](0005-execution-semantics.md)); Execute Workflow already does that. Two gaps remain: cutting many items into groups inside one workflow, and slowing down the calls of a node that makes one call per item (rate limits of an API).

A node can't slow down the next one: every node gets all its items as a list and works them off itself. A "Throttle" node between two nodes would change nothing, so the pause belongs to the nodes that make the calls.

## Decision

- **Split in Batches** (Transform): turns the items into one item per group. Parameters: *Batch size* (default 10) and *Put into field* (default `items`). Each output item is `{ "batch": 1, "batches": 3, "count": 10, "items": [ … ] }`; `batch` counts from 1. Binary data is not carried over, like in Aggregate. No input items give no output items.
- **Pause between calls**: `PipeItemNodeBase` gets `DelayBetweenItems(parameters)` (default none) and waits that long between two items, never before the first or after the last. The wait ends at once when the run is stopped. The HTTP Request node offers it as *Pause between requests (ms)*.
- Execute Workflow gets *Pause between calls (ms)* for its calls in the modes *each item* and *batches*.
- The pause is a fixed time per node, 0 to 600000 ms (ten minutes); no rate window and no sharing between nodes or runs. It adds up with *Timeout*: the timeout of a node covers the pauses too.

## Consequences

- No engine change and no new concept; parameters have a default of 0, so no migration.
- Items still run one after the other; calls in parallel are a topic of their own.
