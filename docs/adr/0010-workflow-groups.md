# 10. Workflow groups

Date: 2026-10-30 · Status: accepted

## Context

With more than a handful of workflows the flat list on the start page gets long. Users want to sort workflows into groups that appear as tiles, and move workflows between them by dragging.

## Decision

- **Folders, not tags:** a workflow is in at most one group (`PipeWorkflow.GroupId`, optional). Groups don't nest.
- **Groups are objects** (`PipeWorkflowGroup`: id, name unique ignoring case, description, one of eight named colors) in their own store, `IPipeWorkflowGroupStore`. The file store keeps them in one file, `data/groups.json`.
  - Workflow files stay flat in `data/workflows/<id>.json`; moving a workflow changes only its `GroupId`. Execute Workflow and error workflows reference ids, so they keep working whatever group a workflow is in.
  - A `GroupId` of a group that doesn't exist (a file copied from another installation) is kept; the workflow is shown without group until such a group exists.
  - Deleting a group, after a confirmation naming the number of workflows, clears their `GroupId`; it never deletes workflows.
- **Start page:** group tiles (alphabetical; name, description, color, number of workflows and active ones, "n failing" for groups whose workflows' last run failed), then the workflows without group. A search lists matching workflows of all groups, with their group.
- **Group page** `/groups/{id}`: the group's workflows; "New workflow" creates them in the group; "All workflows" in the breadcrumb is a drop target that takes a workflow out of its group.
- **Moving:** drag a row onto a tile (HTML drag and drop), or "Move to group…" in the row, or the Group field in the editor's workflow panel (also usable with touch and keyboard).
- **Components:** `PipeWorkflowList` covers all three views (`ShowGroups`, `GroupId`, or neither) so the data is loaded once and tiles and rows update together; navigation stays with the host (`OnOpen`, `OnOpenGroup`, `OnOpenAll`, and `PipeEditor.OnClose` with the group id).
- **Samples** are seeded into the group "Samples" (`PipeFileStoreOptions.SeedGroupsPath`); existing data is not changed.

## Consequences

- Group ids are stable, so groups can become the unit for permissions when users arrive; that is not decided yet.
- Tiles are sorted by name only; there is no manual order.
