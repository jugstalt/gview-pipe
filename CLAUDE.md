# gView.Pipe

n8n-like workflow app on Blazor (.NET 10). The structure follows the sibling repo gview-gis. Terms are defined in [GLOSSARY.md](GLOSSARY.md) and decisions are recorded in [docs/adr](docs/adr).

## Projects (`src/`)

- `gView.Pipe.Abstraction`: plugin SDK (contracts, attributes, models).
  - No UI dependencies, and it must stay small: plugins share it with the host.
- `gView.Pipe.Core`: engine, plugin catalog/loader, expression evaluator, scheduler, trigger manager, memory and file stores.
  - Headless, no Blazor.
- `gView.Pipe.AspNetCore`: ASP.NET Core integration. `app.MapPipeWebhooks()` maps the webhook route (see ADR 0008). This is the only place with HTTP types; Core stays free of ASP.NET.
- `gView.Pipe.Plugins`: the built-in nodes and credential types.
  - They use only the Abstraction contracts, exactly like external plugins.
  - Ids are in `PipeBuiltIn.cs` and must never change.
- `gView.Pipe.Plugins.Database`: the SQL node, as an external plugin with its own dependencies (SqlClient, Npgsql, SQLite).
  - Building it copies the output into `src/gView.Pipe.Shell/plugins/gView.Pipe.Plugins.Database/` (ignored by Git).
  - Values reach SQL only as parameters; the SQL text is read from `RawParameters` and never evaluated.
- `gView.Pipe.Plugins.Scripts`: Run Scripts (bat/cmd, PowerShell, Python, sh) from released script folders or Git repositories (git CLI); see ADR 0011. Installed into the Shell like the database plugin.
- `gView.Pipe.Razor`: MudBlazor UI components.
  - Depends on Abstraction only.
  - `Components/`: `PipeWorkflowList` (start page with group tiles and search via `ShowGroups`, a group page via `GroupId`), `PipeCredentialList`.
  - `Editor/`: `PipeEditor` on Z.Blazor.Diagrams.
  - `Forms/`: forms generated from parameter classes.
  - Embedding hosts need the diagram CSS/JS and must render without prerendering (see ADR 0004).
  - Navigation is the host's job: components raise events like `OnOpen` and `OnClose`.
- `gView.Pipe.Shell`: the app itself. It contains only layout, routing and service composition (`AddPipeCore().AddPipeFileStore().AddPipeRazor()`).
- `Aspire/`: AppHost and ServiceDefaults.
- `tests/`: xUnit v3 test projects, run through Microsoft.Testing.Platform (see `global.json`). `gView.Pipe.Razor.Tests` uses bUnit with MudBlazor services.

## Conventions

- Namespaces mirror folders and use file-scoped declarations.
- Interface folders are named `Abstraction` (singular).
- DI registration lives in `static public class ServicesExtensions` in the namespace `<Project>.Extensions.DependencyInjection`. Its `Add…` methods return `IServiceCollection`.
- The code style is `static public` / `static private`, as in gview-gis.
- Public types are prefixed with `Pipe`, e.g. `PipeEngine`, `IPipeNode`.
- Package versions live only in `Directory.Packages.props` (central package management). Common properties are in `Directory.Build.props`.
- JSON is camelCase with enums as camelCase strings. Always use `PipeJson.Options` (`gView.Pipe.Abstraction.Serialization`), so engine, UI and plugins bind parameters the same way.
- Code, comments and UI text are in English.

## Writing a node

1. Write a class implementing `IPipeNode<TParameters>`, or derive from `PipeItemNodeBase<TParameters>` for per-item logic.
2. Mark it with `[PipeNode("<new guid>", "Name", Category = …)]`.
3. Describe the parameters as a POCO with `[PipeParameter]` and DataAnnotations.
4. For triggers, implement `IPipeTriggerNode`. For ports that depend on parameters, implement `IPipeDynamicPorts`. When parameters change, increase `Version` and implement `IPipeNodeMigration`.
5. Constructor injection works. A new instance is created per execution.
6. To run JavaScript, inject `IPipeScriptRunner` and pass `context.CreateExpressionScope(item, index)`. Don't reference Jint from plugins.
7. Code fields are marked `[PipeParameter(Code = true)]` and read from `context.RawParameters`, because `GetParameters` evaluates `{{ }}` in every string.

## Commands

```bash
dotnet build gView.Pipe.slnx
dotnet test --solution gView.Pipe.slnx
dotnet run --project src/gView.Pipe.Shell
dotnet run --project src/Aspire/gView.Pipe.AppHost
```

The Shell keeps its runtime state in `src/gView.Pipe.Shell/data/`, which Git ignores; never commit it. It loads external plugins from `plugins/<Name>/<Name>.dll`.

## Dev containers

The AppHost can start optional containers. They are switched on by uncommenting the `#define` lines at the top of `src/Aspire/gView.Pipe.AppHost/AppHost.cs`, as in webgis. They are persistent and not wired into the Shell: enter hosts and ports in the Pipe credentials yourself.

- `ADD_MAILPIT`: Mailpit for the Send Mail node, with SMTP on `localhost:1025` and the UI at http://localhost:8025.
- `ADD_GITEA`: Gitea for Run Scripts with Git, at http://localhost:3000 (SSH on port 2222). Login is `gitea`/`gitea`.
- `ADD_IDENTITYSERVER`: IdentityServer.NET at https://localhost:8443 with admin password `admin`. Client `local-gview-pipe`/`secret` for `https://localhost:7240` is prepared for future authentication.

Sample workflows live in `samples/workflows/`. They are seeded into an empty `data/workflows/` on first start, and the plugin tests run them too.
