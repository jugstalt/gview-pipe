# 31. Workflow versions

Date: 2026-11-03 · Status: accepted

## Context

The audit log ([ADR 0022](0022-audit-log.md)) says who changed a workflow and gives a summary, but an earlier state can't be brought back. Undo/redo of the editor ends with the session.

## Decision

- Every save that changes a workflow stores a copy of the saved state as a version. A save that changes nothing (same JSON apart from `updatedAt`) adds none.
- `IPipeWorkflowVersionStore` (Abstraction): `ListAsync(workflowId)` (newest first, with time and a summary of the change against the version before, `PipeWorkflowChanges`), `GetAsync(workflowId, versionId)`.
- The file store writes `data/workflow-versions/<workflowId>/<yyyyMMddHHmmssfff>.json` (the version id is the time stamp). Secret variables stay protected, like in the workflow file. Deleting the workflow deletes its versions.
- `Pipe:WorkflowVersions` (default 50, 0 = no versions): older versions are removed at each save.
- *Restore* in the workflow panel loads a version into the editor like an unsaved change, so it can be checked, undone and then saved; the save becomes a new version. Nothing is overwritten.
- The REST API lists versions: `GET /api/v1/workflows/{id}/versions`.
- Export/import with Git is not part of this; the CLI deploy ([ADR 0026](0026-cli.md)) already brings workflow files into a Git repository.

## Consequences

- The memory store keeps no versions.
- Versions are copies, not deltas: 50 versions of a large workflow take room; the limit is the control.
