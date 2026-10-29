# gView.Pipe

n8n-like workflow app on Blazor (.NET 10). The structure follows the sibling repo gview-gis. Terms are defined in [GLOSSARY.md](GLOSSARY.md) and decisions are recorded in [docs/adr](docs/adr).

## Projects (`src/`)

- `gView.Pipe.Abstraction`: plugin SDK (contracts, attributes, models).
  - No UI dependencies, and it must stay small: plugins share it with the host.
- `gView.Pipe.Core`: engine, plugin catalog/loader, expression evaluator, scheduler, trigger manager, memory and file stores.
  - Headless, no Blazor.
- `gView.Pipe.Plugins`: the built-in nodes and credential types.
  - They use only the Abstraction contracts, exactly like external plugins.
  - Ids are in `PipeBuiltIn.cs` and must never change.
- `gView.Pipe.Razor`: MudBlazor UI components.
  - Depends on Abstraction only and is render-mode agnostic.
- `gView.Pipe.Shell`: the app itself. It contains only layout, routing and service composition (`AddPipeCore().AddPipeFileStore().AddPipeRazor()`).
- `Aspire/`: AppHost and ServiceDefaults.
- `tests/`: xUnit v3 test projects, run through Microsoft.Testing.Platform (see `global.json`).

## Conventions

- Namespaces mirror folders and use file-scoped declarations.
- Interface folders are named `Abstraction` (singular).
- DI registration lives in `static public class ServicesExtensions` in the namespace `<Project>.Extensions.DependencyInjection`. Its `Add…` methods return `IServiceCollection`.
- The code style is `static public` / `static private`, as in gview-gis.
- Public types are prefixed with `Pipe`, e.g. `PipeEngine`, `IPipeNode`.
- Package versions live only in `Directory.Packages.props` (central package management). Common properties are in `Directory.Build.props`.
- JSON is camelCase with enums as camelCase strings. Always use `PipeJson.Options`.
- Code, comments and UI text are in English.

## Writing a node

1. Write a class implementing `IPipeNode<TParameters>`, or derive from `PipeItemNodeBase<TParameters>` for per-item logic.
2. Mark it with `[PipeNode("<new guid>", "Name", Category = …)]`.
3. Describe the parameters as a POCO with `[PipeParameter]` and DataAnnotations.
4. For triggers, implement `IPipeTriggerNode`. For ports that depend on parameters, implement `IPipeDynamicPorts`. When parameters change, increase `Version` and implement `IPipeNodeMigration`.
5. Constructor injection works. A new instance is created per execution.

## Commands

```bash
dotnet build gView.Pipe.slnx
dotnet test --solution gView.Pipe.slnx
dotnet run --project src/gView.Pipe.Shell
dotnet run --project src/Aspire/gView.Pipe.AppHost
```

The Shell keeps its runtime state in `src/gView.Pipe.Shell/data/`, which Git ignores; never commit it. It loads external plugins from `plugins/<Name>/<Name>.dll`.

Sample workflows live in `samples/workflows/`. They are seeded into an empty `data/workflows/` on first start, and the plugin tests run them too.
