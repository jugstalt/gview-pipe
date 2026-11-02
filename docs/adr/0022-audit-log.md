# 22. Audit log

Date: 2026-11-02 · Status: accepted

## Context

With roles ([ADR 0021](0021-roles.md)) and API keys ([ADR 0020](0020-rest-api.md)) several people and scripts change one instance. Nothing records who changed, deleted or ran what.

## Decision

### What is recorded

| Area | Actions |
|---|---|
| workflow | created, saved (with a summary of the change), deleted, activated, deactivated, moved, deployed (API) |
| group | created, changed, deleted |
| credential | created, changed, deleted (never values) |
| variables | the instance variables changed (names; never values of secret ones) |
| apiKey | created, deleted |
| login | signed in; failed sign-ins and refused API keys, at most one entry per minute and address |
| execution | started by hand or through the API, stopped, replayed |

Triggered runs are not recorded: the execution history has them.

- A saved workflow gets a summary of what changed: nodes added, removed and changed (parameters, settings, name), connections, settings of the workflow (`PipeWorkflowChanges`). Full copies of earlier versions belong to workflow versions, a topic of its own.

### Who

The actor is the user name of the login, `api:<key name>`, `anonymous` with the client address in the mode *none*, or `system`. `IPipeAuthorization` gives the user name with the roles (`PipeUserRoles.UserName`).

### Storage

- `IPipeAuditLog` (Abstraction). The file store appends JSON lines to `data/audit/<yyyy-MM>.jsonl`; without it the entries stay in memory.
- `Pipe:AuditRetentionDays` (default 365): the daily cleanup of [ADR 0015](0015-execution-history-and-replay.md) deletes older month files.
- Every entry is also logged (`ILogger`, category `gView.Pipe.Audit`), so a central log system keeps it. There is no protection against changes of the files: who can write `data/` can change anything; for proof use the central log.

### Views

- Page *Audit* (admin): filters for time, actor, area and object; more rows on demand; CSV export of what is shown.
- The workflow panel of the editor lists the last changes of the workflow (*History*), for everyone who sees the workflow.
- `GET /api/v1/audit` for API keys with *read* that are not limited to groups.

## Consequences

- Components that change data write the entry themselves, after the change succeeded; the stores don't, because engine and triggers use them too.
- `IPipeUserContext` (always anonymous) is replaced by the user name of `IPipeAuthorization`.
