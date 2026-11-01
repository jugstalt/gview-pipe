# Writing a node

A node is a class that implements `IPipeNode<TParameters>` and carries a `[PipeNode]` attribute. Reference `gView.Pipe.Abstraction` only.

## A first node

```csharp
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;

namespace MyCompany.PipeNodes;

public sealed class ShoutNodeParameters
{
    [PipeParameter(Label = "Field", Description = "The text field to turn to upper case")]
    public string Field { get; set; } = "name";
}

[PipeNode("9d6c1c3e-6a60-4b3a-9c43-0f6b6a6f2b11", "Shout",
    Category = "Transform",
    Description = "Turns a text field of each item to upper case.")]
public sealed class ShoutNode : PipeItemNodeBase<ShoutNodeParameters>
{
    protected override Task<PipeItem?> ProcessItemAsync(
        PipeItem item, ShoutNodeParameters parameters, IPipeNodeContext context, CancellationToken cancellationToken)
    {
        if (item.Json[parameters.Field]?.GetValue<string>() is { } text)
        {
            item.Json[parameters.Field] = text.ToUpperInvariant();
        }

        return Task.FromResult<PipeItem?>(item);   // null drops the item
    }
}
```

1. **The attribute.** `[PipeNode("<guid>", "Name")]` registers the class. Create a **new GUID** for every node and never change it: workflows store the id, not the class name, so you may rename and move classes freely. `Category` decides the palette group, `Description` the tooltip and the node reference.
2. **The parameters.** A class with a parameterless constructor. The editor generates the form from its public properties ([Parameters and the editor form](parameters.md)).
3. **The logic.** `PipeItemNodeBase<T>` calls `ProcessItemAsync` for every item, with the parameters already evaluated for that item (`{{ }}` expressions resolved). For anything else implement `IPipeNode<T>` yourself:

```csharp
public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
{
    var items = context.GetInput();                       // all items of the main input
    var parameters = context.GetParameters<MyParameters>(items.FirstOrDefault());
    // ...
    return Task.FromResult(PipeNodeResult.FromMain(result));
}
```

A **new instance** of the class is created for every execution and constructor injection works, so don't keep state between runs. Services come from the application's container (plugins can add their own, see [Plugins](plugins.md)).

## The context

`IPipeNodeContext` is everything a node needs:

| Member | Purpose |
|---|---|
| `GetInput(port)` | The items that arrived on an input port (default `main`). |
| `GetParameters<T>(item, index)` | The parameters with all `{{ }}` expressions evaluated against `item`. |
| `RawParameters` | The stored parameters, expressions **not** evaluated. |
| `Evaluate(template, item)` | Evaluates a template string such as `"Hello {{ $json.name }}"`. |
| `GetCredentialAsync<T>(id)` | The decrypted data of a credential, bound to your credential class. |
| `Logger` | Writes to the application log and to the node's **Logs** tab. |
| `BinaryStore` | Reads and writes the bytes of binary attachments. |
| `ExecuteWorkflowAsync(id, items)` | Runs another workflow as a sub-workflow. |
| `WorkflowId`, `WorkflowName`, `ExecutionId`, `NodeId`, `NodeName` | Where you are. |
| `Services` | The services of the current execution scope. |

## Items

A `PipeItem` is a `JsonObject` (`item.Json`) plus `Binary`, a dictionary of attachment references by name. The engine clones items on their way into a node, so changing them doesn't affect other branches. To produce a new item, build a new `PipeItem(new JsonObject { ... })`. Keep `Binary` when the item still stands for the same file.

## Ports

By default a node has one input and one output, `main`. Declare others on the attribute:

```csharp
[PipeNode("…", "Split by Size", Outputs = new[] { "small", "large" })]
```

and return the items per port:

```csharp
return PipeNodeResult.FromPorts(("small", small), ("large", large));
```

A port that gets no items stays empty, and nodes behind it are skipped. If the number of ports depends on the parameters (like Switch), implement `IPipeDynamicPorts`: `GetInputs` and `GetOutputs` receive the stored parameters and return the ports. Nodes with the error mode *Route items to an error output* get an extra `error` port from the engine.

## Expressions in code

Parameters are evaluated for you. To run **JavaScript** from a node (a Code-like node), inject `IPipeScriptRunner`:

```csharp
var scope = context.CreateExpressionScope(item, index);
var result = _scripts.Run(code, scope, log: line => context.Logger.LogInformation("{Line}", line), cancellationToken: cancellationToken);
```

Don't reference the JavaScript engine (Jint) from plugins. A **code** field must not be evaluated as an expression: mark it `[PipeParameter(Code = true)]` and read it from `context.RawParameters`, because `GetParameters` evaluates `{{ }}` in every string.

## Triggers

A trigger starts executions. Implement `IPipeTriggerNode` next to `IPipeNode<T>`:

```csharp
public Task<IAsyncDisposable?> ActivateAsync(IPipeTriggerContext context, CancellationToken cancellationToken)
{
    var parameters = context.GetParameters<MyTriggerParameters>();
    var registration = context.Host.RegisterSchedule("*/5 * * * *", TimeZoneInfo.Utc,
        ct => context.FireAsync([new PipeItem()], ct));
    // registration is an IDisposable that removes the schedule; wrap it in an IAsyncDisposable
    return Task.FromResult<IAsyncDisposable?>(new Handle(registration));
}
```

- `ActivateAsync` is called when an active workflow is saved or the app starts. Return a handle that disarms the trigger.
- `context.Host` offers `RegisterSchedule` (cron) and `RegisterWebhook` (HTTP, served by `MapPipeWebhooks()`).
- `FireAsync` starts an execution at this trigger and completes when it has finished. It returns `null` when the workflow's limit of concurrent runs skipped the run.
- `ExecuteAsync` of a trigger usually just passes the trigger items on: `PipeNodeResult.FromMain(context.GetInput())`.
- `IPipeSubWorkflowTrigger` and `IPipeErrorTrigger` are markers for the entry points of sub-workflows and error workflows.

## Errors

- Throw `PipeException` for expected problems with a message the user can act on ("Select a connection"). The message appears on the node.
- Throw `PipeNodeException(message, items)` when you have result items worth keeping even though the node failed (for example the output of scripts that ran). With the error mode *Continue* or *Error output* the engine passes those items on instead of the input items.
- Respect `cancellationToken`; the user can stop a run.
- Retries and timeouts are done by the engine according to the node's settings.

## Hints

`IPipeNodeHints.GetHints(parameters)` returns notes the editor shows above the parameters, for example the URL of a webhook or a warning about a missing credential.

## Changing parameters later

Increase `Version` on the attribute when you rename or restructure parameters, and implement `IPipeNodeMigration`. The engine calls `MigrateParameters(fromVersion, parameters)` before running a node whose workflow was saved with an older `typeVersion`.

## Values that depend on the server

A parameter can offer values that the server computes from the other parameters (for example the files of a folder): mark it with `[PipeOptions]` and implement `IPipeParameterOptions` on the node. A `string` becomes a choice of one option, a `List<PipeSelection>` an ordered list whose entries can be switched on and off. `Refreshable = true` adds a refresh button.

## Testing

`src/tests/gView.Pipe.Plugins.Tests/PluginTestHost.cs` builds a host with the built-in nodes, in memory, and runs workflows. Use the same approach for your plugin: register your assembly in `PipeCoreOptions.PluginAssemblies` and run a workflow made with the `PipeWorkflowBuilder` of `gView.Pipe.Core` or loaded from a JSON file.
