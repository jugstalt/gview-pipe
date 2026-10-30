# gView.Pipe Glossary

| Term | Meaning | Code |
|---|---|---|
| **Workflow** | A stored graph of nodes and connections; one JSON document. Only DAGs (no cycles). | `PipeWorkflow` |
| **Node** | An instance of a node type inside a workflow, with its own parameters and settings. Has a unique `id` (for connections) and a unique `name` (for expressions). | `PipeWorkflowNode` |
| **Node type** | The logic behind nodes, provided by a plugin class with `[PipeNode("guid", …)]`. Workflows reference it by that GUID. | `IPipeNode`, `PipeNodeDescriptor` |
| **Type version** | Parameter version of a node type. Older workflow nodes are migrated before execution. | `PipeNodeAttribute.Version`, `IPipeNodeMigration` |
| **Port** | Named input or output of a node. The default port is `main`; `error` is added when a node routes failures to an output. | `PipePort`, `PipePorts` |
| **Connection** | Links an output port of one node to an input port of another. | `PipeConnection` |
| **Item** | The unit of data on a connection: a JSON object plus optional binary attachments. Nodes receive and emit lists of items. | `PipeItem` |
| **Binary** | File content attached to an item. The item only holds a reference; the bytes live in the execution's binary store. | `PipeBinaryRef`, `IPipeBinaryStore` |
| **Parameters** | Configuration of a node. Declared as a POCO class (`IPipeNode<TParameters>`), stored as JSON. String values may contain expressions. | `PipeParameterAttribute` |
| **Expression** | `{{ … }}` inside a parameter value: JavaScript, evaluated per item in a sandbox. Variables: `$json`, `$input`, `$node["Name"].json` / `.all`, `$itemIndex`, `$now`, `$workflow`, `$execution`. | `IPipeExpressionEvaluator` |
| **Script** | A JavaScript function body run by a node (e.g. Code), with the same variables as expressions. | `IPipeScriptRunner` |
| **Trigger** | A node type that starts executions (manual, cron, webhook, sub-workflow call, error). Has no inputs. | `IPipeTriggerNode` |
| **Sub-workflow** | A workflow run by the Execute Workflow node of another execution, starting at its Execute Workflow Trigger. Shares the caller's binary store; nesting is limited (default 10). Calling it per item or per batch is how loops are built. | `IPipeNodeContext.ExecuteWorkflowAsync`, `IPipeSubWorkflowTrigger` |
| **Error workflow** | Workflow named in another workflow's settings; its Error Trigger runs when a triggered (not manual) run of that workflow fails. | `PipeWorkflowSettings.ErrorWorkflowId`, `IPipeErrorTrigger` |
| **Webhook** | Trigger that starts a workflow when `/webhook/{path}` is called. The path is unique across all active workflows. It answers immediately (202) or with the output of the last node. | `PipeWebhookRegistration`, `IPipeWebhookRouter` |
| **Node hint** | An info or warning a node shows in the editor depending on its parameters (e.g. the webhook URL). | `IPipeNodeHints` |
| **Active workflow** | A workflow whose triggers are armed (e.g. its cron schedule runs). | `PipeWorkflow.Active`, `IPipeTriggerManager` |
| **Execution** | One run of a workflow, with status and output items per node. | `PipeExecution`, `PipeNodeRun` |
| **Start node** | Where an execution begins: the firing trigger, or for manual runs all nodes without incoming connections. Only nodes reachable from start nodes take part. | `PipeExecutionRequest.StartNodeId` |
| **Pin data** | Fixed output items of a node, per output port, stored in the workflow. In manual runs a pinned node is not executed and returns them; triggered runs ignore pin data. | `PipeWorkflow.PinData`, `PipeNodeRun.Pinned` |
| **Run to here** | A manual run limited to one node and the nodes it depends on. | `PipeExecutionRequest.TargetNodeId` |
| **Engine** | Executes a workflow graph; stores nothing. | `IPipeEngine` |
| **Runner** | Runs workflows like the app does: concurrency limit and execution history. | `IPipeWorkflowRunner` |
| **Credential** | Stored, encrypted secret data (API key, password, …) referenced by nodes via its id. | `PipeCredential` |
| **Credential type** | POCO class with `[PipeCredentialType("guid", …)]` describing the fields of a credential; `[Secret]` marks sensitive fields. | `PipeCredentialTypeDescriptor` |
| **Plugin** | An assembly with node types, credential types and optionally an `IPipePluginStartup`. Built-in plugins are loaded from the app folder, external ones from `plugins/<Name>/<Name>.dll` in their own load context. | `PipePluginCatalog` |
