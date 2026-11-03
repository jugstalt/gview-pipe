# 35. OAuth2 client credentials for HTTP Request

Date: 2026-11-03 · Status: accepted

## Context

HTTP Request signs in with Basic, a fixed bearer token or an API key header. Many APIs (Microsoft Graph, cloud services) hand out short-lived access tokens from a token endpoint; today a workflow would have to fetch and pass the token by itself.

## Decision

- A new credential type **OAuth2 Client Credentials** (`b7b000e5-84d5-4cff-8dd3-d3c15df89457`) with *Token URL*, *Client id*, *Client secret* (secret), *Scope* (optional), *Audience* (optional, sent as `audience`) and *Send client credentials* (*in the Authorization header*, the default, or *in the body*). The secret can come from a source like any secret field ([ADR 0033](0033-external-secrets.md)).
- HTTP Request accepts it: it fetches an access token with `grant_type=client_credentials` and sends `Authorization: Bearer <token>`.
- Only this grant. The authorization code flow needs a callback route and a store for refresh tokens and is not part of this; user-bound tokens can still be passed in a bearer credential.
- **Token cache** (`PipeOAuth2TokenCache`, in memory, one for the process): a token is kept per credential and per settings, so editing the credential gets a new token. It is renewed 60 seconds before it expires (`expires_in`, one hour if the answer has none). Concurrent requests share one fetch. Tokens are never written to disk.
- A `401` answer drops the cached token, so the next attempt (a retry of the node, the next run) fetches a new one. The failing request itself is not sent again.
- A failing token request fails the node with the status and the `error` of the answer (shortened); the message never holds the client secret or a token.

## Consequences

- The cache lives in the plugin assembly of the built-in nodes and is registered as a singleton by its startup.
- A token revoked at the provider works from the cache until a `401` or its expiry.
