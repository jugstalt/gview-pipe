# 28. Portal: frontend

Date: 2026-11-02 · Status: accepted

## Context

The portal for users ([ADR 0027](0027-portal-backend.md)) may run on an internet server that has no engine and no tools installed.

## Decision

- Standalone project `gView.Pipe.Portal`: Blazor Server with MudBlazor, depending only on the Abstraction contracts it needs and an `HttpClient`; no Core, engine or plugins.
- Chain: browser → portal server → backend API. The service key lives only in the portal server and never reaches the browser. WebAssembly or a SPA calling the backend directly are excluded.
- The portal does the login of the user (OIDC or forms) and sends the user in every call (delegation). The email for completion mails comes from the `email` claim.
- Configuration: backend URL, key, login. Delivered as ZIP and Docker image.
- The backend is reachable only from the portal (VPN, firewall or private network). If that is not possible: HTTPS with an IP allow-list on the proxy and a key that expires and is rotated.
- Status is polled (2 s while waiting, 10 s in the job list), centrally per circuit; the service interface allows push later.
- Screens: workflows by category with search, form, waiting/background, own jobs with *Running* and *Stop*, later history.

## Consequences

- A compromised portal can act as any user; the key limits the damage to the portal endpoints.
