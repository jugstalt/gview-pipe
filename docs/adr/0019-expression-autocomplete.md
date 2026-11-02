# 19. Expression autocomplete

Date: 2026-11-02 · Status: accepted

## Context

Expression fields show the value of an expression for the last run (`PipeExpressionPreview`), and a click on a field of the run's input inserts `{{ $json.path }}`. While typing, nothing helps: field names, node names and variables have to be known by heart. The fields are MudBlazor text fields; only code fields use Monaco.

## Decision

- **Catalog:** for the selected node the editor builds a list of completions (`PipeCompletionCatalog`): path, type and a short sample value.
  - `$json.…` from the node's input of the shown run; without one, from the outputs (run or pin data) of its direct predecessors. The first 20 items, 4 levels, at most 500 entries.
  - `$node["Name"].json.…` and `.all` for every node upstream that has data in the shown run or pin data.
  - `$vars.NAME` for the variables of workflow, group and instance in the workflow's environment; secret ones without a value.
  - `$now`, `$itemIndex`, `$environment`, `$workflow.id/name`, `$execution.id`, `$input.all()/first()`, and in code `items`.
  - JavaScript methods are not listed.
- **Transfer:** the catalog goes to the browser when the selection, the shown run or the pin data change; filtering while typing happens in the browser, without a round trip per key.
- **Expression fields:** the editor's script (`gview-pipe-completion.js`) shows a popup under the field inside `{{ }}`: opened by `{{ `, `.` and `["`, or Ctrl+Space; arrows choose, Enter or Tab inserts at the cursor, Escape closes. Typing `{{` adds the closing `}}`. The text is changed in the input and announced with an `input` event, so Blazor binds it like typed text.
- **Code fields:** a Monaco completion provider for JavaScript serves the same catalog.
- Without data for the node, the popup shows the global names and variables and a hint to run the workflow (or *Run to here*).

## Consequences

- The completions are only as good as the last run; that is also what the preview shows.
- One catalog at a time: the editor shows one node panel.
