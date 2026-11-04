# Changelog

All notable changes to gView.Pipe are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the versions follow [Semantic Versioning](https://semver.org/). The version of a build is the `<Version>` in [`Directory.Build.props`](Directory.Build.props).

Release notes with upgrade hints for administrators are in the [documentation](docs/site/en/release-notes.md). Design decisions behind the changes are in [`docs/adr`](docs/adr).

Keep the `Unreleased` section up to date with every change; on a release, rename it to the new version and date.

## [Unreleased]

The first release, 0.1.0. Until it is tagged, everything below is part of it.

### Added

**Editor**
- Visual workflow editor on a canvas with a node palette and parameter forms generated from the parameter classes of the nodes. Undo and redo, copy and paste (also between workflows), duplicate, insert a node onto a connection, resizable side panels and sticky notes (ADR 0004, 0024).
- The workflow panel is divided into sections you can open and close. A ? button explains each section and links to the matching page of the documentation; the toolbar, the panel header, the node panel and the app bar link to the documentation too. The address is `Pipe:DocsUrl`, by default the built-in `/docs` or the public documentation.
- Code fields in Monaco, expression autocomplete, a dark mode (ADR 0019).
- Workflow versions: every changing save keeps a copy; the workflow panel lists them with a summary and restores one into the editor; `GET /api/v1/workflows/{id}/versions` (ADR 0031).
- Secrets from other sources: credential fields and variables can reference an environment variable, Azure Key Vault or the secrets vault of IdentityServer.NET (plugin `gView.Pipe.Plugins.Secrets`); read when a run needs them, cached in memory, masked in logs and history (ADR 0033).
- OpenTelemetry metrics of the engine under the meter `gView.Pipe`: runs, duration, skipped and active runs per workflow (ADR 0032).
- Inspect every node: input, output and log of the latest run or of any execution; items as JSON or as text; live output while a node runs. Pin the output of a node, run to a node, replay a run from a node (ADR 0015).

**Engine and nodes**
- Execution engine for graphs without cycles: parallel branches, retry, timeout and per-node error handling (stop, continue, error output), a queue for triggered runs (ADR 0005, 0016).
- Split in Batches node (one item per group of items) and a pause between requests or calls in HTTP Request and Execute Workflow for rate limits (ADR 0034).
- OAuth2 client credentials credential for HTTP Request: fetches an access token from a token endpoint, keeps it in memory until shortly before it expires and sends it as a bearer token (ADR 0035).
- File Trigger: starts a workflow when new files appear in a released folder; polls the folder, reports a file once it is complete, one run per check with one item per file (ADR 0036).
- SFTP node (plugin `gView.Pipe.Plugins.Sftp`): list, download, upload, delete and rename files on an SFTP server over one connection; the credential holds the server's host key fingerprint, which is checked; uploads go through a `.part` file (ADR 0037).
- Email Trigger (IMAP) with the credential IMAP Server: starts a workflow when unread mails are in a mailbox; one run per check with one item per mail, attachments as binary data; the mails are marked as read, moved or deleted once the run has started (ADR 0038).
- SFTP Trigger (plugin `gView.Pipe.Plugins.Sftp`): starts a workflow when files appear in a folder on an SFTP server; polls, reports a file once it is complete, one run per check with one item per file; the files are moved or deleted once the run has started, or only new ones are reported (ADR 0039).
- Triggers read credentials like nodes do: fields that reference an external secret source are resolved (ADR 0033), so e.g. the credential of a Webhook Trigger can take its password from a vault.
- JavaScript expressions `{{ … }}` and the Code node, run in a sandbox (Jint) (ADR 0007).
- Built-in nodes: triggers (Manual, Cron, Webhook, Form, Error, Recovery, Execute Workflow), flow (If, Switch, Filter, Merge, Wait, Execute Workflow), transform (Set, Code, Sort, Limit, Aggregate, Split Out, Remove Duplicates), network (HTTP Request, Respond to Webhook, Send Email), files, data (file formats, date and time, crypto, compression) and utility nodes (ADR 0009, 0018).
- Webhook triggers with three response modes; Respond to Webhook makes a workflow a small API (ADR 0008).
- Sub-workflows, error workflows and failure and recovery notifications (ADR 0017, 0025).
- Variables and environments, set per instance, group and workflow (ADR 0013).
- Workflow groups with tiles on the start page and a search over all workflows (ADR 0010).
- Credentials, encrypted with ASP.NET Data Protection (ADR 0006).

**Plugins**
- Plugin SDK `gView.Pipe.Abstraction`, external plugins from a folder, each in its own load context (ADR 0003).
- Database plugin: SQL Server, PostgreSQL and SQLite, values only as parameters.
- Scripts plugin: Run Script, Run Scripts Folder and Run Scripts Repo for `.bat`, `.cmd`, `.ps1`, `.py` and `.sh`, from released folders or Git servers (ADR 0011).
- Office plugin: the Excel format (.xlsx) for Extract from File and Convert to File.

**Operation**
- Sign-in of the UI: none, forms users or OpenID Connect, with the roles viewer, operator, editor and admin per instance or group (ADR 0014, 0021).
- REST API with API keys and an OpenAPI description, and the command line tool `gview-pipe` (deploy, run, executions) (ADR 0020, 0026).
- Audit log and execution history with replay (ADR 0022, 0015).
- User portal: a separate app that lets users run published workflows through a form and see the result, with roles, completion mail and history (ADR 0027–0030).
- Settings in `_config/pipe.config` and `_config/auth.config` (ADR 0012).
- Deploy build: ZIP for Windows and Linux and a Docker image (Nuke), and an Aspire AppHost with optional dev containers.

**Documentation**
- MkDocs site in English and German (user, admin and developer guide, node reference generated from the node attributes), served by the Shell under `/docs` and published at https://docs.gviewonline.com/other/gview-pipe/.
- `GLOSSARY.md`, architecture decisions in `docs/adr` and `THIRD-PARTY-NOTICES.md`. A test fails when a package is missing in the notices.
