# Developer Guide

This guide is for people who extend gView.Pipe with their own nodes and credential types, or embed it in another app. It is available in English only.

- [Writing a node](writing-nodes.md): the contract, a first node, ports, triggers, errors.
- [Parameters and the editor form](parameters.md): how a parameter class becomes a form.
- [Plugins](plugins.md): packaging, loading, services and credential types.

## The pieces

| Project | Purpose |
|---|---|
| `gView.Pipe.Abstraction` | The **plugin SDK**: contracts, attributes and models. No UI dependencies, and it stays small: plugins share it with the host. |
| `gView.Pipe.Core` | The headless engine: plugin catalog and loader, expression evaluator, scheduler, trigger manager, stores. |
| `gView.Pipe.AspNetCore` | ASP.NET Core integration: `app.MapPipeWebhooks()`. The only place with HTTP types. |
| `gView.Pipe.Plugins` | The built-in nodes and credential types. They use only the Abstraction, exactly like external plugins. |
| `gView.Pipe.Plugins.Database`, `.Scripts` | External plugins with their own dependencies, examples for plugin code. |
| `gView.Pipe.Razor` | MudBlazor components: workflow list, credential list and the editor. They depend on the Abstraction only. |
| `gView.Pipe.Shell` | The app itself: layout, routing and service composition. |

The decisions behind the design are recorded in [`docs/adr`](https://github.com/jugstalt/gview-pipe/tree/main/docs/adr) in the repository.

## How a run works

1. A **trigger** (manual, cron, webhook, another workflow or an error) starts an execution with one or more **items**.
2. The engine walks the graph, which is a DAG without cycles. A node starts when all the nodes it depends on have finished. Independent branches run in parallel.
3. A node gets **all items** of its inputs, returns items per output port, and the engine hands them to the next nodes. Nodes whose inputs are all empty are skipped.
4. Each node run records its input, output and log. Failures follow the node's error setting (stop, continue, error output).

Items are JSON objects plus optional binary attachments. Nodes need no knowledge of the engine, only of `IPipeNodeContext`.

## Embedding in another Blazor app

```csharp
builder.Services
    .AddPipeCore()
    .AddPipeFileStore(options => options.RootPath = "data")
    .AddPipeRazor();

// ...
app.MapPipeWebhooks();
```

The host references the MudBlazor and Z.Blazor.Diagrams CSS and JavaScript plus `_content/gView.Pipe.Razor/css/gview-pipe.css`, and renders the components interactively without prerendering. Navigation is the host's job: the components raise events such as `OnOpen` and `OnClose`. See `App.razor` of the Shell.

`AddPipeCore()` alone registers in-memory stores, which is what tests use. `AddPipeFileStore()` stores workflows, groups, executions and credentials as JSON files.

## Conventions

- Namespaces mirror folders and use file-scoped declarations. Public types are prefixed with `Pipe`.
- JSON is camelCase, enums are camelCase strings. Always use `PipeJson.Options` so the engine, the UI and plugins bind parameters the same way.
- Node and credential ids are GUIDs that **never change** once published: workflows store them.
- Code, comments and UI text are in English.
