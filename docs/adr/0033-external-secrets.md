# 33. External secrets

Date: 2026-11-03 · Status: accepted

## Context

Secret values of credentials and variables are encrypted with Data Protection and kept in files under `data/` ([ADR 0021](0021-roles.md) lists who may read them in the UI). For containers, CI and central secret management the values must not live in the file store at all: they should stay in environment variables, Azure Key Vault or the secrets vault of the IdentityServer.NET that the AppHost already starts.

The settings of the host itself (`Pipe:*`, `Pipe:Auth:Oidc:ClientSecret`, API keys) are not part of this: ASP.NET Core resolves them from environment variables or the Azure Key Vault configuration provider already, and the docs say so.

## Decision

### What can come from a source

Fields of credentials and values of variables. A stored value is either a plain value or a reference, written as the text `secret:<source>/<name>` (for example `secret:vault1/db-password`). One string works for the string fields of credentials and for the values of variables alike; the UI shows source and name apart. A plain value can't start with `secret:`.

- The reference is the whole value of the field; there is no expression and no reference inside a longer string. Code nodes and parameter fields can't read other secrets than the ones of the credentials and variables they use.
- `name` may end in `@<version>` (`db-password@3`); without it the newest version is read. Sources without versions (environment) reject a version.

### Sources

- `IPipeSecretProvider` (Abstraction): `Name`, `GetAsync(name, ct)` returning the value or a `PipeSecretNotFound` result, and a check that reads without returning the value.
- Environment is built into Core. The other two are external plugins with their own dependencies, like the database plugin:
  - Azure Key Vault (`Azure.Security.KeyVault.Secrets`, `DefaultAzureCredential`, so managed identity in Azure and the developer login locally).
  - The secrets vault of IdentityServer.NET: `client_credentials` token for the scope `secrets-vault.<locker>`, then `/api/secretsvault?path=<locker>/<secret>[/<version>]` (or `SecretsVaultClient` of `IdentityServerNET.Clients`).

### Configuration

Named sources in `pipe.config`; the credentials to reach a source come from the environment or the platform, never from the UI:

```json
"Pipe": { "Secrets": { "CacheSeconds": 300, "Sources": {
  "env":    { "Type": "env", "Prefix": "PIPE_SECRET_" },
  "vault1": { "Type": "azureKeyVault", "VaultUri": "https://x.vault.azure.net/", "Prefix": "pipe-" },
  "idsrv":  { "Type": "identityServerVault", "Authority": "https://localhost:8443", "ClientId": "...", "ClientSecret": "...", "Locker": "pipe" }
} } }
```

- The source `env` exists without configuration, with the prefix `PIPE_SECRET_`.
- A reference to `db-password` in `env` reads `PIPE_SECRET_DB_PASSWORD` (upper case, `-` and `.` become `_`). The prefix is required: without it a workflow could read any variable of the host. The error message names the variable it looked for.
- What a source can reach is limited by the server configuration: the prefix (environment, Key Vault) or the locker (IdentityServer vault). There is no list per workflow.

### Resolution

- Run time, not save time: the engine resolves the references when it hands the credential or the variables to a node. A changed value in the source is used by the next run.
- A cache in memory keeps a value for `Pipe:Secrets:CacheSeconds` (default 300; 0 = no cache). It is never written to disk and is cleared when the configuration is reloaded.
- A missing secret or an unreachable source fails the run with a message that names source and name, never a value. It counts like any other error: error workflow ([ADR 0017](0017-failure-notifications.md)), the metric `gview.pipe.runs` with `status=error` ([ADR 0032](0032-metrics.md)). Nothing is checked when saving, because a source can differ between development and production.
- Resolved values join the masking that credential values already get in logs, node runs and execution data, so they don't reach the history.

### UI

- Credential form and variable dialog: per field a switch *Value / From source*, a list of the configured sources and a field for the name. After saving only source and name show, never the value.
- A button *Check* reads the secret and says found, not found or unreachable, without showing the value.
- Whoever may edit credentials or variables may reference any name the source allows (see above). The audit log names source and name of a changed reference, never a value ([ADR 0022](0022-audit-log.md)).

### Not part of it

- No Key Vault, locker or secret is created or written by gView.Pipe: the sources are read only.
- The AppHost gets no switch: the migrations of `IdentityServerNET.Aspire.Hosting` create clients, roles and users, but no locker. The docs describe the three steps for the IdentityServer vault (resource `secrets-vault`, locker, API client).

## Consequences

- The file format of credentials and variables gets the reference as a second shape of a value; older files stay valid.
- An external value is as secret as its source: the Shell holds it in memory while it runs.
- A source that is slow slows the first run after the cache expired, not every item.
