# 8. Webhook triggers

Date: 2026-10-29 · Status: accepted

## Context

Most real workflows are started from outside by an HTTP call. Core must stay free of ASP.NET, and the webhook route must work in the Shell as well as in other host apps such as gView.WebApps.

## Decision

- **Contracts** (Abstraction): `IPipeTriggerHost.RegisterWebhook(PipeWebhookRegistration)` and `IPipeWebhookRouter`. Requests and responses are plain objects (`PipeWebhookRequest`, `PipeWebhookResponse`) without ASP.NET types.
- **Registry** (Core): `PipeWebhookRegistry` maps a path to exactly one trigger.
  - Paths are trimmed, lower-cased and limited to `[a-z0-9_-/]`.
  - A second registration of the same path fails. `IPipeTriggerManager.ActivateAsync` returns such problems and the editor shows them after saving. The other triggers of the workflow are armed anyway.
- **HTTP** (new project `gView.Pipe.AspNetCore`): `app.MapPipeWebhooks()` maps `/webhook/{**path}` for all methods.
  - It reads query, headers and body: JSON for `*json*` content types, form fields as an object, uploads as files, anything else as text.
  - The body limit defaults to 10 MB; larger requests get 413.
  - Responses are written as JSON with relaxed escaping.
- **Webhook Trigger node:**
  - Parameters: path (random by default), method (default POST), response mode and timeout, optional credential.
  - Credentials: Basic, Bearer or API-key header, compared in constant time. A missing or wrong credential returns 401.
  - Status codes: 404 for unknown paths, 405 for a wrong method, 429 when the concurrency limit skips the run, 500 with the execution id when the run fails.
- **Request item:** `{ method, path, query, headers, body, receivedAt }`.
  - `authorization`, `proxy-authorization`, `cookie` and the API-key header are dropped, so secrets never reach the execution history.
  - Uploaded files become binary attachments. Their bytes travel with `PipeTriggerFireOptions.BinaryContent` into the execution's binary store, not as base64 in the item.
- **Response modes:**
  - "Immediately" answers 202 with `executionId`.
  - "When finished" waits for the run, up to the timeout. It answers with the items of the node that finished last: one item as an object, several as an array. After the timeout it answers 202 and the run continues.
  - Runs are never canceled when the caller disconnects.
- **Editor hints:** Nodes can show hints next to their parameters (`IPipeNodeHints`). The webhook uses this for its URL and for a warning when it is public.

## Consequences

- Webhooks only work while the workflow is **active** (saved with "Active"). There is no test URL yet: the editor cannot wait for a call during a manual run.
- The hint always shows the default prefix `/webhook`. A host that maps another prefix must tell its users.
- Webhook paths are global across the whole app, not per user (there are no users yet).
