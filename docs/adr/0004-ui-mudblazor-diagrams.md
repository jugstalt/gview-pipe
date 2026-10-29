# 4. UI: MudBlazor plus Z.Blazor.Diagrams

Date: 2026-10-29 · Status: accepted

## Context

The UI library of a Razor class library is forced on every host app that embeds it. gview-gis uses MudBlazor. Fluent UI Blazor was considered because its flat look resembles Power Automate and Logic Apps.

## Decision

- MudBlazor (aligned with gview-gis) is used for layout, dialogs, forms and tables. A flat look comes from `PipeTheme`.
- The workflow canvas will use Z.Blazor.Diagrams. Its nodes are Razor components, so plugins can later ship their own node UI.
- Parameter forms are generated from the parameter POCO (`[PipeParameter]`, DataAnnotations, `[Secret]`, `[PipeCredential]`). A plugin can optionally supply its own Razor editor.

## Implementation (editor)

- **`PipeEditor`:** palette, canvas and side panel. The canvas owns topology and positions, while `PipeWorkflowNode` keeps parameters and settings. On save and run, the workflow is rebuilt from both.
- **Links:** they may be drawn from either end. `PipePortModel.CanAttachTo` only allows output↔input between different nodes, and connections are normalized to output → input.
- **Ports:** ports come from `IPipeNodeRegistry`, so dynamic ports (Switch cases) and the `error` port follow parameter and setting changes. On load, ports referenced by stored connections are kept, so links of unknown node types survive.
- **Live status:** a run from the editor passes its own `IPipeExecutionObserver`, which pushes node states onto the canvas. Old executions can be shown again from the history.
- **Forms:** `PipeParameterForm` uses the metadata from `PipeParameterFields`. Credential secrets are never sent back to the browser, and an empty secret field means "unchanged".
- **JSON options:** `PipeJson` moved to `gView.Pipe.Abstraction`, so UI and engine bind parameters identically while Razor still depends only on Abstraction.

## Consequences

- Embedding in gView.WebApps adds no second design system.
- Host apps must include `_content/Z.Blazor.Diagrams/style.min.css`, `_content/Z.Blazor.Diagrams/script.min.js` and `_content/gView.Pipe.Razor/css/gview-pipe.css`. They must also render the editor interactively **without prerendering**: `DiagramCanvas` calls JS when it is disposed.
- Z.Blazor.Diagrams ports measure themselves before the canvas knows its container. The editor therefore re-renders all nodes on `ContainerChanged`; without this, links are not drawn.
