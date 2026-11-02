# Ideas

Backlog of possible features. Internal, not part of the docs site. Larger items get an ADR before they are built.

Suggested next: the GIS nodes.

## Operations and reliability

- [x] Execution history of all workflows and replay (ADR 0015).
- [x] Retry and timeout per node and "continue on error" existed already; exponential backoff added (ADR 0015).
- [ ] Queue for triggered runs: `MaxConcurrentRuns` exists, but surplus trigger and webhook runs are skipped; let them wait in a bounded queue instead (replays already wait).
- [ ] Failure notifications: a ready-made Error Trigger workflow that reports by mail or Teams/Slack.
- [ ] Metrics with OpenTelemetry: runs, duration and errors per workflow (the ServiceDefaults are already there).

## Nodes

- [ ] Data and formats: CSV, XML and Excel read/write, JSON path or mapping, date and time, crypto/hash, ZIP.
- [ ] Flow: loop / split in batches, rate limit or throttle, sub-workflow with return value (check what Execute Workflow already covers).
- [ ] Integrations: FTP/SFTP, S3 or Azure Blob, Teams/Slack webhook, REST with an OAuth2 credential, MQTT or RabbitMQ.
- [ ] Triggers: file watcher (folder), incoming mail (IMAP), queue trigger.
- [ ] gView-specific: nodes for gView GIS and WebGIS, such as feature queries, geometry operations, coordinate transformation, GeoJSON.
- [ ] AI: an LLM node that classifies, extracts or summarizes items.

## Editor and UX

- [ ] Pinned data: keep test data on a node so the trigger doesn't have to fire every time.
- [ ] Undo/redo, copy/paste of nodes (also as JSON between workflows), sticky notes and comments on the canvas.
- [ ] Expression editor: autocomplete for `{{ $json… }}` and a preview of the evaluated value.
- [ ] Workflow versions: history and diff, export/import with Git.

## Security and multi-user

- [ ] Finer roles than "signed in": viewer, editor, admin, also per group.
- [ ] Audit log: who changed or ran what and when.
- [ ] Secrets from Key Vault or environment variables, not only from the file store.

## Platform

- [ ] Database store (SQLite or Postgres) as an alternative to the file store, for many runs.
- [ ] Plugin packaging: install plugins as NuGet packages from the UI.
- [ ] REST API or CLI: start, list and deploy workflows from outside.
