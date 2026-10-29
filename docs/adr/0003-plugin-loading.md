# 3. Plugins: attribute with stable GUID, one AssemblyLoadContext per plugin folder

Date: 2026-10-29 · Status: accepted

## Context

Node logic comes from plugins. gview-gis identifies plugins by a `[RegisterPlugIn("guid")]` attribute and loads them with `Assembly.LoadFrom`. gView.Pipe must not depend on gview-gis. Plugins may also bring dependency versions that differ from other plugins.

## Decision

- Node types are marked with `[PipeNode("guid", "Name")]` and credential types with `[PipeCredentialType("guid", "Name")]`.
- Workflows store the GUID, never a type name, so renaming or moving classes doesn't break workflows.
- Built-in nodes (`gView.Pipe.Plugins`) are found by scanning the app folder for `gView.Pipe.Plugins*.dll`. They go through the same discovery path as external plugins.
- An external plugin is installed as `plugins/<Name>/<Name>.dll`. Each one is loaded into its own `AssemblyLoadContext` and resolves its dependencies from its `deps.json`.
- Assemblies matching `PipeCoreOptions.SharedAssemblyPrefixes` always come from the host: `gView.Pipe.Abstraction`, `Microsoft.Extensions.*`, `Microsoft.AspNetCore.*` and `System.*`. This way plugin and host share the contract types.
- Plugins are loaded while `AddPipeCore()` runs, so `IPipePluginStartup.ConfigureServices` can register services before the container is built.
- Plugins cannot be unloaded.

## Consequences

- `gView.Pipe.Abstraction` must stay small and stable. It is published as the plugin SDK, and plugins reference it with `ExcludeAssets=runtime`.
- Nodes from a plugin that isn't installed still load in a workflow. Executing them fails with "Unknown node type", and the node data is kept.
- Razor UI from external plugins needs static asset serving from the plugin folder. That comes with the editor.
