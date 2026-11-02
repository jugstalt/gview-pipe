# 27. Portal: backend

Date: 2026-11-02 · Status: accepted

## Context

Users who may only run workflows (not edit or deploy them) have the REST API ([ADR 0020](0020-rest-api.md)) and the CLI ([ADR 0026](0026-cli.md)), both too technical and too broad. A web portal for them is planned ([ADR 0028](0028-portal-frontend.md)). The backend runs where the tools are installed; the portal may run on an internet server.

## Decision

- **Endpoints** under `/api/v1/portal/*` (`app.MapPipeApi()`): published workflows with category and form schema, start, own runs, status, cancel, result. A key with only the right `portal` can call nothing else. Details of nodes, parameters and credentials are never returned.
- **Publishing:** a workflow is visible only with the switch *Publish in portal* in its settings. The group is the category.
- **Form Trigger** node declares the inputs: text, multiline text, number, boolean, date, choice; password fields are never stored. File upload comes later. The backend validates every start against the current form (`422` with the list of fields); the portal is not trusted.
- **Result node** defines the output: text/markdown, table or file download, and an own error message. Without it only the status is shown. Raw items and node errors never reach portal users; a failed run shows a generic message and the execution id.
- **Run mode:** the author sets the default (wait with timeout, or background) and whether the user may switch. After the timeout *wait* turns into background.
- **Delegation:** a `portal` key may send `X-Pipe-On-Behalf-Of: <user>`; the execution gets `StartedBy = portal:<user>`. Lists and cancel see only that user's runs. Admins can still stop everything in the editor. The author can forbid stopping for a workflow.
- **Limits:** concurrent runs per user (default 3, configurable), a workflow option *single run at a time*, and the queue of [ADR 0016](0016-queued-runs.md) (`429` when full).
- **Roles later:** the endpoints already pass the user (and later roles) through; an empty role list on a workflow or group means everybody.

## Consequences

- New: `PipeApiRights.Portal`, Form Trigger and Result nodes, publish flag, the audit log records portal starts with the user.
- The backend trusts a `portal` key to name the right user; a stolen key can act as any user, so the backend should be reachable only from the portal host.
- Implemented: the Form Trigger takes its fields as a JSON array for now (a field editor can follow); the Result node does text, Markdown and table, file downloads come later. The limit per user is `Pipe:Portal:MaxRunsPerUser`.
