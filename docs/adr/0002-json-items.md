# 2. Untyped JSON items between nodes

Date: 2026-10-29 · Status: accepted

## Context

Data between nodes can be typed (typed ports, validated in the editor) or untyped (like n8n: lists of JSON items).

## Decision

- A connection carries a list of `PipeItem`. Each item holds a `JsonObject` plus binary references.
- A node receives all items of all input ports at once.
- `PipeItemNodeBase<T>` covers the common per-item case.
- The bytes of binary attachments are kept in a per-execution `IPipeBinaryStore`: in memory up to a threshold, in temp files above it.
- The engine clones items on the way into a node, so branches cannot affect each other.

## Consequences

- Plugins stay simple, and everything serializes trivially (execution history, pin data).
- The editor cannot check type compatibility when connecting nodes. Port types may be added later as hints only.
- Binary references in a stored execution do not point to anything once the execution has finished.
