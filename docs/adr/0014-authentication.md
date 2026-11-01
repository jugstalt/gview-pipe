# 14. Authentication

Date: 2026-11-01 · Status: accepted

## Context

The UI was open to everyone who could reach the address. Two setups are needed: small installations with a few fixed users, as in the gview-gis web apps, and installations with an identity provider (IdentityServer.NET for tests). In Docker, a password must be easy to set by an environment variable.

## Decision

- One mode per instance, `Pipe:Auth:Mode`: `none` (default), `forms` or `oidc`. No mix; a local emergency user next to OIDC is not supported.
- The settings live in their own file `_config/auth.config` (prototype `_setup/proto/auth.config`), loaded after `pipe.config` and before environment variables (ADR 0012).
- Only the UI is protected: the Razor component endpoints and the Blazor hub carry the policy `gview-pipe-user` (`RequirePipeAuthorization()`). Webhooks, `/docs`, static files and health checks stay anonymous. Webhooks are protected by their own credentials.
- Signed in means allowed to do everything; there are no roles inside the app yet.
- The code is in `gView.Pipe.AspNetCore` (`AddPipeAuthentication`, `UsePipeAuthentication`, `MapPipeAuthentication`), so Core and Razor stay free of it. The pages `/account/login` and `/account/denied` belong to the Shell. They are static SSR pages (`[ExcludeFromInteractiveRouting]`), because the cookie can only be set by an HTTP response, not from a circuit.
- Session: cookie `gview-pipe.auth`, sliding, `SessionMinutes` (default 480).
- **Forms:** users in `Pipe:Auth:Users` with `PasswordHash` (PBKDF2-SHA256, `--hash-password`) or `Password` (plain text, logs a warning; the hash wins). `PIPE_ADMIN_PASSWORD` (and optionally `PIPE_ADMIN_USER`, default `admin`) adds or replaces that user and switches `none` to `forms`. The login post checks the antiforgery token, is rate limited per client address (`LoginAttemptsPerMinute`, default 10) and waits a second after a wrong password. No lockout per account, so nobody can lock out the admin.
- **OIDC:** authorization code with PKCE, client secret optional, scopes configurable (default `openid profile role`). `RoleClaimType` (default `role`) and `RequiredRoles`: one of them is enough; an empty list lets every authenticated user in. A user without a required role is signed in but gets `/account/denied`, which offers the logout. The logout also ends the session at the provider.
- `UseForwardedHeaders` (default off) trusts `X-Forwarded-*` of any proxy, so OIDC builds https redirect URIs behind a reverse proxy.

## Consequences

- Existing installations and development keep running without a login (`none`, with a warning in the log).
- Embedding hosts can reuse the three extension methods or bring their own authentication.
- The IdentityServer.NET of the AppHost needs the role `gview-pipe` and test users, created in its admin UI.
- The docs screenshots run with `none`.
