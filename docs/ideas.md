# Ideas

Backlog of possible features. Internal, not part of the docs site. Larger items get an ADR before they are built.

Priority: everything gView.Pipe needs apart from geo; the GIS nodes come last. Suggested next: the REST API.

## Operations and reliability

- [x] Execution history of all workflows and replay (ADR 0015).
- [x] Retry and timeout per node and "continue on error" existed already; exponential backoff added (ADR 0015).
- [x] Queue for triggered runs (ADR 0016).
- [x] Failure notifications: inherited error workflow, throttling, Mail/Teams/Slack samples (ADR 0017).
- [ ] "Back to normal" message when a workflow that failed succeeds again (needs the last state per workflow).
- [ ] Metrics with OpenTelemetry: runs, duration and errors per workflow (the ServiceDefaults are already there).

## Nodes

- [x] Data and formats: Extract from File / Convert to File (CSV, JSON, XML, Text, Excel via the Office plugin), Date & Time, Crypto, Compression (ADR 0018).
- [ ] More formats through `IPipeFileFormat`: e.g. YAML, fixed-width text, PDF text extraction.
- [ ] Flow: loop / split in batches, rate limit or throttle, sub-workflow with return value (check what Execute Workflow already covers).
- [ ] Integrations: FTP/SFTP, S3 or Azure Blob, Teams/Slack webhook, REST with an OAuth2 credential, MQTT or RabbitMQ.
- [ ] Triggers: file watcher (folder), incoming mail (IMAP), queue trigger.
- [ ] AI: an LLM node that classifies, extracts or summarizes items.

## Editor and UX

- [x] Pinned data, undo/redo and copy/paste of nodes (as JSON, also between workflows) exist already.
- [ ] Sticky notes and comments on the canvas.
- [x] Expression editor: autocomplete in expression and code fields (ADR 0019); the preview of the value existed already.
- [ ] Workflow versions: history and diff, export/import with Git.

## Security and multi-user

- [ ] Finer roles than "signed in": viewer, editor, admin, also per group.
- [ ] Audit log: who changed or ran what and when.
- [ ] Secrets from Key Vault or environment variables, not only from the file store.

## Platform

- [ ] Database store (SQLite or Postgres) as an alternative to the file store, for many runs.
- [ ] Plugin packaging: install plugins as NuGet packages from the UI.
- [ ] REST API or CLI: start, list and deploy workflows from outside.

## GIS (last)

Postponed until the approach is clear.

- [ ] gView-specific nodes: feature queries, geometry operations, coordinate transformation, GeoJSON. Open questions: data sources through gView's `IDataset` / `IFeatureClass`, features as GeoJSON in the items, geometry functions from `spatialNet.Core`? Plugin with its own dependencies, like the database plugin?
