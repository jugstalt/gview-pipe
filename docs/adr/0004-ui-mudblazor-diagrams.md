# 4. UI: MudBlazor plus Z.Blazor.Diagrams

Date: 2026-10-29 · Status: accepted

## Context

The UI library of a Razor class library is forced on every host app that embeds it. gview-gis uses MudBlazor. Fluent UI Blazor was considered because its flat look resembles Power Automate and Logic Apps.

## Decision

- MudBlazor (aligned with gview-gis) is used for layout, dialogs, forms and tables. A flat look comes from `PipeTheme`.
- The workflow canvas will use Z.Blazor.Diagrams. Its nodes are Razor components, so plugins can later ship their own node UI.
- Parameter forms are generated from the parameter POCO (`[PipeParameter]`, DataAnnotations, `[Secret]`, `[PipeCredential]`). A plugin can optionally supply its own Razor editor.

## Consequences

- Embedding in gView.WebApps adds no second design system.
- The editor (step 2) adds the Z.Blazor.Diagrams dependency to `gView.Pipe.Razor`.
