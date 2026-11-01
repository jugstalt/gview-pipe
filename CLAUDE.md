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
- `gView.Pipe.Plugins.Scripts`: Run Script, Run Scripts Folder and Run Scripts Repo (bat/cmd, PowerShell, Python, sh) from released script folders or configured Git servers (Gitea/GitHub API, temporary clones via git CLI); see ADR 0011. Installed into the Shell like the database plugin.
- `gView.Pipe.Razor`: MudBlazor UI components.
  - Depends on Abstraction only.
  - `Components/`: `PipeWorkflowList` (start page with group tiles and search via `ShowGroups`, a group page via `GroupId`), `PipeCredentialList`.
  - `Editor/`: `PipeEditor` on Z.Blazor.Diagrams.
  - `Forms/`: forms generated from parameter classes.
  - Embedding hosts need the diagram CSS/JS and must render without prerendering (see ADR 0004).
  - Code fields use Monaco (`PipeCodeField`, BlazorMonaco). Hosts load `_content/BlazorMonaco/jsInterop.js`, `.../lib/monaco-editor/min/vs/loader.js` and `.../vs/editor/editor.main.js` before the Blazor script, as in the Shell's `App.razor`. A dark host cascades `true` named `PipeDarkMode` and sets `data-mud-theme="dark"` around the components for the dark variables of `gview-pipe.css`; the Shell's `MainLayout` does both (system, light or dark, kept in localStorage).
  - The editor's splitters are dragged in the browser (`wwwroot/js/gview-pipe-resize.js`); .NET only hears the final width.
  - Navigation is the host's job: components raise events like `OnOpen` and `OnClose`.
- `gView.Pipe.Docs`: build tool, generates the node reference of the docs from the node attributes. Not shipped.
- `gView.Pipe.Docs.Screenshots`: build tool, takes the screenshots of the docs with Playwright. Not shipped.
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
pip install -r docs/requirements.txt   # once, for the docs
dotnet run --project src/gView.Pipe.Docs   # generate the node reference once
python -m mkdocs serve                 # live preview of the docs
./build.cmd Docs                       # static site in artifacts/docs/site
```

The Shell keeps its runtime state in `src/gView.Pipe.Shell/data/`, which Git ignores; never commit it. It loads external plugins from `plugins/<Name>/<Name>.dll`.

## Documentation

The user and admin documentation is a MkDocs Material site in `docs/site/` (`en/` is the source, `de/` the translation), configured by `mkdocs.yml`. `docs/adr` is internal and not part of the site.

- A change to a node, a parameter, the configuration or the behavior of the app updates the matching pages in both languages in the same change. A missing German page falls back to English with a warning; don't leave it that way on purpose.
- Links inside the site are relative, so the same site runs at `/docs` in the Shell and at the root of a docs host.
- `docs/site/downloads/workflows/` (the samples offered on the recipes page) and `docs/site/en/user-guide/nodes/` (the node reference) are generated from `[PipeNode]` and `[PipeParameter]` by `src/gView.Pipe.Docs`, never edited or committed. Write the `Description` of nodes and parameters for readers of the docs; run `dotnet run --project src/gView.Pipe.Docs` before `mkdocs serve`. The German site falls back to the English pages.
- The Shell serves the built site under `/docs` (`PipeDocsSite`, folder `docs` next to the app or `Pipe:DocsPath`), and the Compile target of the deploy build copies it into the ZIP and the Docker image. The Docs link in `MainLayout.razor` shows only when the folder exists. To try it locally: `Pipe__DocsPath=<repo>/artifacts/docs/site`.
- Python is needed for the docs only. `dotnet build` and `dotnet test` don't use it.
- Images in `docs/site/images/` are English only; the UI has no other language. They are generated: `./build.cmd DocsScreenshots` (`src/gView.Pipe.Docs.Screenshots`) starts the Shell with an empty temporary data folder and takes them with Playwright. Run it after a UI change and commit the images. Chromium is needed once: `dotnet run --project src/gView.Pipe.Docs.Screenshots -- --install`. A new picture is a method in `DocsScreenshots.cs`.

## Dev containers

The AppHost can start optional containers. They are switched on by uncommenting the `#define` lines at the top of `src/Aspire/gView.Pipe.AppHost/AppHost.cs`, as in webgis. They are persistent and not wired into the Shell: enter hosts and ports in the Pipe credentials yourself.

- `ADD_MAILPIT`: Mailpit for the Send Mail node, with SMTP on `localhost:1025` and the UI at http://localhost:8025.
- `ADD_GITEA`: Gitea for Run Scripts with Git, at http://localhost:3000 (SSH on port 2222). Login is `gitea`/`gitea`.
- `ADD_IDENTITYSERVER`: IdentityServer.NET at https://localhost:8443 with admin password `admin`. Client `local-gview-pipe`/`secret` for `https://localhost:7240` is prepared for future authentication.

Sample workflows live in `samples/workflows/`. They are seeded into an empty `data/workflows/` on first start, and the plugin tests run them too.
