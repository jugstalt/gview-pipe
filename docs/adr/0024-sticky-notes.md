# 24. Sticky notes

Date: 2026-11-02 · Status: accepted

## Context

Workflows grow, and the reasons behind them (why this filter, what to set up first) live in people's heads. A node has a note field, but it is visible only when the node is selected.

## Decision

- A workflow has an optional list `stickyNotes` (`PipeStickyNote`: id, text, color, position, width, height), separate from the nodes. Engine, validation and *Execute Workflow* don't know it; files without the list load as before.
- The text is plain text with line breaks. (Markdown was considered; it needs a renderer package and sanitizing, and can follow later.)
- Six colors (yellow, green, blue, red, purple, gray), defined for the light and the dark theme in `gview-pipe.css`.
- On the canvas a note lies behind nodes and links, has no ports and does not move other nodes. Select it to edit text, color and size in the side panel; drag it like a node.
- *Add note* in the toolbar puts a note in the middle of the visible canvas. Copy, paste, duplicate, delete, undo and redo include notes (the clipboard content has a `notes` list next to `nodes`).
- Editing follows the editor role ([ADR 0021](0021-roles.md)); viewers and operators see notes read only.
- The audit log summary names a change of notes as `notes`, not each text (`PipeWorkflowChanges`).
- The samples carry a note where a setup hint helps; the docs show one in a screenshot.

## Consequences

- Older gView.Pipe versions keep the list in their extension data and write it back unchanged.
- Notes are not versioned or searched separately: they are part of the workflow document.
