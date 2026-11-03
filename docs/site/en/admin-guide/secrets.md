# Secrets from other sources

Credentials and variables can take a secret from an environment variable, Azure Key Vault or the secrets vault of IdentityServer.NET instead of keeping the value in `data/`. The value is read when a workflow runs; a changed value is used by the next run (after the cache time).

## Configure the sources

The sources are named in `_config/pipe.config`. Their access data comes from the environment or the platform, never from the UI.

```json
"Pipe": {
  "Secrets": {
    "CacheSeconds": 300,
    "Sources": {
      "vault": { "Type": "azureKeyVault", "VaultUri": "https://my-vault.vault.azure.net/", "Prefix": "pipe-" },
      "idsrv": { "Type": "identityServerVault", "Authority": "https://identity.example.com", "Locker": "pipe", "ClientId": "pipe-secrets", "ClientSecret": "..." }
    }
  }
}
```

Put the `ClientSecret` into an environment variable (`Pipe__Secrets__Sources__idsrv__ClientSecret`) rather than into the file. `CacheSeconds` is how long a value stays in memory (never on disk); `0` reads it every time.

| Source | Type | Settings |
|---|---|---|
| `env` | built in, needs no configuration | `Prefix` (default `PIPE_SECRET_`). The secret `db-password` is the environment variable `PIPE_SECRET_DB_PASSWORD`. |
| Azure Key Vault | `azureKeyVault`, plugin `gView.Pipe.Plugins.Secrets` | `VaultUri`, `Prefix` (default `pipe-`: the secret `db-password` is `pipe-db-password` in the vault). Signs in with `DefaultAzureCredential`: a managed identity in Azure, the developer login locally. |
| IdentityServer.NET | `identityServerVault`, plugin `gView.Pipe.Plugins.Secrets` | `Authority`, `Locker`, `ClientId`, `ClientSecret`. |

What a workflow can reach is limited by the server: the prefix for the environment and Key Vault, the locker of the client for the IdentityServer vault.

## Use a secret

- **Credential:** the key button in a secret field takes the value from a source. After saving, source and name show instead of the value.
- **Variable:** the key button in the value of an environment does the same.

In both dialogs, *Check* tells whether the secret can be read without showing it. In the file the reference is the text `secret:<source>/<name>`, for example `secret:vault/db-password`; with `@3` behind the name a version is read (`secret:vault/db-password@3`), otherwise the newest. The `env` source has no versions. A plain value can't start with `secret:`.

Values read from a source are masked in logs, node output and the execution history like secret variables. If a secret is missing or the source can't be reached, the run fails with a message that names source and secret, never the value.

## Set up the vault of IdentityServer.NET

The AppHost doesn't create these; in the admin UI of the IdentityServer:

1. As system admin create the API resource `secrets-vault`.
2. Create a locker (for example `pipe`) and add the secrets to it.
3. Create an API client with a client secret and the scopes `secrets-vault` and `secrets-vault.pipe`. Its data go into the source. `IdentityServer:PublicOrigin` must be set on the IdentityServer, otherwise it rejects every token.

## Settings of the host

The settings of the app itself (`Pipe:Auth:Oidc:ClientSecret`, API keys, …) are not read through these sources. ASP.NET Core reads them from environment variables or the Azure Key Vault configuration provider (`AddAzureKeyVault`) on its own.
