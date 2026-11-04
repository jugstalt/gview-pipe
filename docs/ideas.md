# Ideas

Backlog of possible features. Internal, not part of the docs site. Larger items get an ADR before they are built.

Priority: everything gView.Pipe needs apart from geo; the GIS nodes come last. Suggested next: S3 or Azure Blob, or the LLM node.

## Operations and reliability

- [x] Execution history of all workflows and replay (ADR 0015).
- [x] Retry and timeout per node and "continue on error" existed already; exponential backoff added (ADR 0015).
- [x] Queue for triggered runs (ADR 0016).
- [x] Failure notifications: inherited error workflow, throttling, Mail/Teams/Slack samples (ADR 0017).
- [x] "Back to normal" message when a workflow that failed succeeds again: Recovery Trigger (ADR 0025).
- [x] Metrics with OpenTelemetry: runs, duration and errors per workflow (ADR 0032).

## Nodes

- [x] Data and formats: Extract from File / Convert to File (CSV, JSON, XML, Text, Excel via the Office plugin), Date & Time, Crypto, Compression (ADR 0018).
- [ ] More formats through `IPipeFileFormat`: e.g. YAML, fixed-width text, PDF text extraction.
- [x] Flow: Split in Batches and a pause between requests or calls (ADR 0034); loops and sub-workflows with a return value existed already through Execute Workflow.
- [ ] Integrations: S3 or Azure Blob, FTP/FTPS (SFTP node and trigger are done, ADR 0037, 0039), Teams/Slack webhook, MQTT or RabbitMQ. (REST with an OAuth2 client credentials credential is done, ADR 0035; the authorization code flow is not part of it.)
- [x] Triggers: File Trigger for a folder (ADR 0036).
- [x] Triggers: incoming mail through IMAP polling (ADR 0038); OAuth2 sign-in and IDLE are not part of it.
- [ ] Triggers: queue trigger.
- [ ] AI: an LLM node that classifies, extracts or summarizes items.

## Editor and UX

- [x] Pinned data, undo/redo and copy/paste of nodes (as JSON, also between workflows) exist already.
- [x] Sticky notes on the canvas (ADR 0024); comments by several people are not part of it.
- [x] Expression editor: autocomplete in expression and code fields (ADR 0019); the preview of the value existed already.
- [x] Workflow versions: history with a summary of each change and restore (ADR 0031); a side-by-side diff and export/import with Git are not part of it.

## Security and multi-user

- [x] Roles viewer, operator, editor, admin, also per group (ADR 0021).
- [ ] Roles in the UI: a user store, so admins can manage users and roles without editing auth.config.
- [x] Audit log: who changed, deleted, ran or signed in, with page, CSV export and API (ADR 0022).
- [x] Secrets from Key Vault, the IdentityServer vault or environment variables, not only from the file store (ADR 0033).

## Platform

- [ ] Database store (SQLite or Postgres) as an alternative to the file store, for many runs.
- [ ] Plugin packaging: install plugins as NuGet packages from the UI.
- [x] REST API with API keys: run, read executions, deploy workflows (ADR 0020).
- [x] CLI on top of the REST API: `gview-pipe` tool (ADR 0026); profiles and `--delete-missing` are not part of it.

## GIS (last)

Postponed until the approach is clear.

- [ ] gView-specific nodes: feature queries, geometry operations, coordinate transformation, GeoJSON. Open questions: data sources through gView's `IDataset` / `IFeatureClass`, features as GeoJSON in the items, geometry functions from `spatialNet.Core`? Plugin with its own dependencies, like the database plugin?
