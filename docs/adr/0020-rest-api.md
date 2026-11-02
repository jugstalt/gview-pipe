# 20. REST API

Date: 2026-11-02 · Status: accepted

## Context

Workflows can only be run, deployed and inspected through the UI. Scripts and CI pipelines need an HTTP API; they can't sign in with a cookie, and OIDC client credentials would be too much for most installations.

## Decision

### Endpoints

- Under `/api/v1/` (`app.MapPipeApi()` in `gView.Pipe.AspNetCore`), JSON with `PipeJson.Options`, errors as `application/problem+json`.
- **read:** `GET /workflows`, `GET /workflows/{id}`, `GET /executions` (filters `workflowId`, `status`, `mode`, `from`, `before`, `limit`), `GET /executions/{workflowId}/{id}`, `GET /groups`.
- **run:** `POST /workflows/{id}/run` with an optional body `{ items, environment, wait, timeoutSeconds }`, `POST /executions/{workflowId}/{id}/cancel`, `POST /executions/{workflowId}/{id}/replay` (`fromNodeId`).
- **deploy:** `PUT /workflows/{id}` creates or replaces; `DELETE /workflows/{id}`; `POST /workflows/{id}/activate` and `/deactivate`.
- `GET /api/v1/openapi.json` describes them (`Microsoft.AspNetCore.OpenApi`); there is no UI for it.

### Runs

- New execution mode `Api`. It behaves like `Trigger`: pin data is ignored, the error workflow starts on failure, the queue (`MaxQueuedRuns`, [ADR 0016](0016-queued-runs.md)) applies. It starts at every node without an incoming connection, with the posted items as their input.
- Executions get `StartedBy` (`api:<key name>`), shown in the history.
- `wait: true` answers when the run ended (`200` with status, error and the items of the last node that ran) or with `202` after the timeout; otherwise `202 { executionId }` right away. A full queue answers `429`.

### Deploying

- `PUT` validates before saving (`IPipeWorkflowValidator`): the graph (unique ids and names, known connections, no cycles), installed node types and the parameters (DataAnnotations). Problems answer `422` with a list; credentials that don't exist are warnings in the answer, not errors.
- An optional `If-Match` with the ETag of `GET` (the workflow's `updatedAt`) answers `412` when the stored workflow changed since.
- Active workflows have their triggers armed again after `PUT`.

### API keys

- Sent as `X-Pipe-Api-Key` or `Authorization: Bearer`. The API never uses the cookie of the UI; it needs a key even in the authentication mode *none*.
- Format `pipe_` + 32 random bytes (Base64url), so secret scanners recognize it. Only its SHA-256 is stored (`data/api-keys.json`); the key is shown once when created.
- Each key has a name, rights (`read`, `run`, `deploy`), optionally a list of groups it is limited to, an optional expiry, who created it and when it was last used.
- A limited key sees and changes only workflows in its groups; workflows without a group are outside of every limited key.
- More than 60 failed attempts per minute from one address answer `429`.
- Keys are managed on the page *API keys* by every signed-in user (there are no roles yet).

## Consequences

- `PipeExecutionMode.Api` and `PipeExecution.StartedBy` are new in stored executions.
- Credentials, variables and groups can't be changed through the API.
