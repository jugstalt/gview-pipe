# 9. Files, database and email

Date: 2026-10-30 · Status: accepted

## Context

Workflows need to exchange files, talk to databases and send notifications. All three reach outside the process. Workflows are edited in the browser, so a node must not give its users more access to the server than the host intends.

## Decision

- **Files:**
  - The host releases folders by name (`PipeCoreOptions.FileRoots`, in the Shell `Pipe:FileRoots`). Read File and Write File take a root name and a relative path.
  - `IPipeFileRoots.Resolve` (Abstraction contract, Core implementation) rejects unknown roots, absolute paths and paths that leave the root (`..`). The check lives in one place, not in every node.
  - Formats: binary attachment, text, JSON (object or array of objects) and CSV (RFC 4180, own small parser; no dependency).
- **Database:**
  - One generic SQL node in its own plugin project, `gView.Pipe.Plugins.Database`, with SQL Server (Microsoft.Data.SqlClient), PostgreSQL (Npgsql) and SQLite (Microsoft.Data.Sqlite).
  - It is an external plugin on purpose: it tests plugin loading with real third-party and native dependencies. Building it copies it into the Shell's plugin folder.
  - The plugin loader takes a `System.*` or `Microsoft.Extensions.*` assembly from the host only if the host has it; otherwise from the plugin folder (SqlClient brings `System.*` packages the host doesn't ship).
  - The connection comes from the Connection String credential; its provider field (or the node) selects the driver.
  - Values from items go in as `@name` parameters only. The SQL text is taken from the raw parameters and never evaluated, so expressions can't build SQL.
- **Email:** Send Email uses MailKit over SMTP with a new SMTP credential type (host, port, security, login, default sender). One mail per item, one connection per run, attachments from the item's binaries.
- **Vulnerable transitive packages** (`System.Security.Cryptography.Pkcs` of MimeKit and SqlClient) are lifted by direct references, like MessagePack in the AppHost.

## Consequences

- Without released folders the file nodes only report which folders exist (none). Symbolic links inside a root are not resolved; a host should not release folders containing links to elsewhere.
- The database plugin folder is about 40 MB, mostly native SQLite and SQL Server libraries for all platforms. A host that targets one platform can publish it for that runtime only.
- Receiving mail (IMAP) and other databases (e.g. Oracle) are not covered yet.
