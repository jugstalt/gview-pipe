# gView.Pipe

A workflow automation app in the style of n8n, built on Blazor and .NET 10. Workflows are graphs of nodes that pass JSON items along. A workflow starts manually, on a schedule or through a webhook. Nodes come from plugins, and the editor is a set of Razor components that other Blazor apps can embed.

![Workflow editor after a run](docs/images/editor-run.png)

## Features

- **Visual editor.** Build workflows on a canvas (Z.Blazor.Diagrams) with a node palette and parameter forms generated from the node's parameter class (MudBlazor).
- **Execution engine.** Runs the graph as a DAG. Parallel branches, Switch/If/Merge, retry, timeout and error handling per node (stop, continue or route to an error output).
- **Inspect every node.** Each node shows the input it received, the output it produced and the log entries it wrote, for the latest run and for any execution in the history. Click a property of the input to insert it as an expression into the parameter you edited last.
- **Fast iteration.** Pin a node's output so manual runs reuse it instead of calling the API again, run only up to a node ("run to here"), duplicate nodes and copy/paste them (Ctrl+C / Ctrl+V on the canvas) within or between workflows.
- **JavaScript expressions.** `{{ $json.total * 1.2 }}` in any parameter, plus a Code node for larger transformations. Both run in a sandboxed Jint engine.
- **Triggers.** Manual, Cron and Webhook.
- **Credentials.** Stored encrypted with ASP.NET Data Protection. Nodes reference them by id and never see them in the workflow JSON.
- **Plugins.** A node is a class implementing `IPipeNode<TParameters>`. External plugins are loaded from a folder, each into its own `AssemblyLoadContext`.
- **Storage.** Workflows, executions and credentials are plain JSON files.

| Code node | Node logs | Webhook request as input |
|---|---|---|
| ![Code node](docs/images/editor-code.png) | ![Node logs](docs/images/editor-logs.png) | ![Webhook](docs/images/webhook.png) |

![Workflow list](docs/images/workflows.png)

## Getting started

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/gView.Pipe.Shell
```

Open the URL printed in the console (http://localhost:5240 with the `http` profile). On first start, the sample workflows from [`samples/workflows`](samples/workflows) are copied into the empty store:

- **Hello Pipe**: a manual trigger, Set and Log.
- **Code Transform**: generates orders with JavaScript, routes them by country with Switch, adds VAT and merges them again.
- **Webhook Demo**: answers `POST /webhook/demo` with a greeting. It is inactive until you switch it on.

To run the Shell with the Aspire dashboard (logs, traces, metrics):

```bash
dotnet run --project src/Aspire/gView.Pipe.AppHost
```

### Configuration

`src/gView.Pipe.Shell/appsettings.json`:

| Key | Default | Meaning |
|---|---|---|
| `Pipe:DataPath` | `data` | Folder for `workflows/`, `executions/`, `credentials/` and the Data Protection `keys/`. |
| `Pipe:PluginPath` | `plugins` | Folder with external plugins, one sub folder per plugin: `plugins/<Name>/<Name>.dll`. |

Further options (execution retention, script limits, binary thresholds) are on `PipeCoreOptions` and `PipeFileStoreOptions` and can be set in `Program.cs`.

## Projects

| Project | Purpose |
|---|---|
| `gView.Pipe.Abstraction` | Plugin SDK: contracts, attributes and models. Small and free of UI dependencies, shared by host and plugins. |
| `gView.Pipe.Core` | Headless engine, plugin loader, JavaScript engine, scheduler, trigger manager, file and memory stores. |
| `gView.Pipe.AspNetCore` | ASP.NET Core integration: `app.MapPipeWebhooks()`. |
| `gView.Pipe.Plugins` | Built-in nodes and credential types. They use only the Abstraction, exactly like external plugins. |
| `gView.Pipe.Razor` | MudBlazor components: workflow list, credential list and the editor. |
| `gView.Pipe.Shell` | The app: layout, routing and service composition. |
| `Aspire/` | AppHost and ServiceDefaults. |
| `tests/` | xUnit v3 tests for Core, Plugins and the Razor components (bUnit). |

### Embedding in another Blazor app

```csharp
builder.Services
    .AddPipeCore()
    .AddPipeFileStore(options => options.RootPath = "data")
    .AddPipeRazor();

// ...
app.MapPipeWebhooks();
```

The host must reference the MudBlazor and Z.Blazor.Diagrams CSS/JS plus `_content/gView.Pipe.Razor/css/gview-pipe.css`, and render the components interactively without prerendering. See [`App.razor`](src/gView.Pipe.Shell/Components/App.razor) and [ADR 0004](docs/adr/0004-ui-mudblazor-diagrams.md).

## Built-in nodes

| Category | Nodes |
|---|---|
| Triggers | Manual Trigger, Cron Trigger, Webhook Trigger |
| Flow | If, Switch, Filter, Merge |
| Transform | Set, Code |
| Network | HTTP Request |
| Utility | Log, No Operation |

Credential types: Basic Auth, Bearer Token, API Key Header and Connection String.

## Expressions

Every string parameter can contain `{{ … }}` blocks with JavaScript. A parameter that consists of a single block keeps the type of the result (number, object, array); otherwise the results are inserted into the text.

```text
Hello {{ $json.customer }}!
{{ Math.round($json.total * 1.2 * 100) / 100 }}
{{ $node["Generate Orders"].all.length }}
```

| Variable | Value |
|---|---|
| `$json` | JSON of the current item |
| `$itemIndex` | Index of the current item |
| `$input.all()`, `$input.first()` | All items of the node's input |
| `$node["Name"].json`, `$node["Name"].all` | Output of an earlier node |
| `$workflow`, `$execution` | Information about the workflow and the running execution |
| `$now` | Current time as ISO string |

The **Code** node runs a script either once for all items (`items` holds them, return an array) or once per item (`$json`, return an object). `console.log` writes to the node's logs.

```js
// items: the JSON of all input items
return items
  .filter(order => order.total > 50)
  .map(order => ({ ...order, priority: order.total > 200 }));
```

Scripts run with no access to .NET, the file system or the network, and are limited in time, memory, statements and recursion (`PipeCoreOptions.Scripts`). See [ADR 0007](docs/adr/0007-javascript-expressions.md).

## Webhooks

A Webhook Trigger listens on `/webhook/{path}`. The path is unique across all workflows, and the webhook is armed only while its workflow is active. The request becomes an item with `method`, `path`, `query`, `headers` and `body`; uploaded files become binaries. The `Authorization` and `Cookie` headers are stripped. Optionally the trigger checks a credential.

With response mode **When finished**, the caller gets the output of the last node. With **Immediately**, the caller gets `202 Accepted` and the execution id.

With the Webhook Demo workflow active:

```bash
curl -X POST http://localhost:5240/webhook/demo -H "Content-Type: application/json" -d '{"name":"Ada"}'
```

PowerShell:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5240/webhook/demo -ContentType "application/json" -Body '{"name":"Ada"}'
```

See [ADR 0008](docs/adr/0008-webhooks.md) for status codes and limits.

## Writing a node

Reference `gView.Pipe.Abstraction`, describe the parameters as a POCO and implement the node:

```csharp
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using Microsoft.Extensions.Logging;

namespace MyCompany.Pipe.Nodes;

public sealed class UppercaseParameters
{
    [PipeParameter(Label = "Field", Placeholder = "name")]
    public string Field { get; set; } = "name";
}

[PipeNode("0d6f3c1e-8a52-4f0b-9b7e-3c2a1d4e5f60", "Uppercase",
    Category = "Transform",
    Description = "Converts a text field to upper case.")]
public sealed class UppercaseNode : PipeItemNodeBase<UppercaseParameters>
{
    protected override Task<PipeItem?> ProcessItemAsync(PipeItem item, UppercaseParameters parameters,
        IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var result = item.Clone();
        if (result.Json[parameters.Field]?.GetValue<string>() is { } text)
        {
            result.Json[parameters.Field] = text.ToUpperInvariant();
        }
        context.Logger.LogInformation("Converted {Field}", parameters.Field);

        return Task.FromResult<PipeItem?>(result);
    }
}
```

- The GUID in `[PipeNode]` identifies the node type in saved workflows. Never change it.
- Parameter values are evaluated per item, so `Field` may itself be an expression.
- For several outputs, triggers, dynamic ports or parameter migrations, see the built-in nodes in [`src/gView.Pipe.Plugins`](src/gView.Pipe.Plugins) and [CLAUDE.md](CLAUDE.md#writing-a-node).

Build the plugin and copy the output to `plugins/MyCompany.Pipe/MyCompany.Pipe.dll` next to the Shell. Don't ship `gView.Pipe.Abstraction.dll` or `Microsoft.Extensions.*` with it; these always come from the host.

## Development

```bash
dotnet build gView.Pipe.slnx
dotnet test --solution gView.Pipe.slnx
```

Tests run on Microsoft.Testing.Platform (see `global.json`). The sample workflows are executed by the plugin tests, so a broken sample fails the build.

Terms are defined in [GLOSSARY.md](GLOSSARY.md), design decisions are in [docs/adr](docs/adr).

## License

[Apache License 2.0](LICENSE). Third-party components and their licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
