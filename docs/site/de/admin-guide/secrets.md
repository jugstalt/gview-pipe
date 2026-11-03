# Secrets aus anderen Quellen

Credentials und Variablen können ein Secret aus einer Umgebungsvariablen, aus Azure Key Vault oder aus dem Secrets-Vault von IdentityServer.NET übernehmen, statt den Wert in `data/` zu halten. Der Wert wird gelesen, wenn ein Workflow läuft; ein geänderter Wert gilt ab dem nächsten Lauf (nach der Cache-Zeit).

## Quellen einrichten

Die Quellen stehen mit Namen in `_config/pipe.config`. Ihre Zugangsdaten kommen aus der Umgebung oder von der Plattform, nie aus der Oberfläche.

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

Lege das `ClientSecret` besser in eine Umgebungsvariable (`Pipe__Secrets__Sources__idsrv__ClientSecret`) als in die Datei. `CacheSeconds` ist die Zeit, die ein Wert im Speicher bleibt (nie auf der Platte); bei `0` wird jedes Mal gelesen.

| Quelle | Type | Einstellungen |
|---|---|---|
| `env` | eingebaut, braucht keine Konfiguration | `Prefix` (Standard `PIPE_SECRET_`). Das Secret `db-password` ist die Umgebungsvariable `PIPE_SECRET_DB_PASSWORD`. |
| Azure Key Vault | `azureKeyVault`, Plugin `gView.Pipe.Plugins.Secrets` | `VaultUri`, `Prefix` (Standard `pipe-`: das Secret `db-password` heißt im Vault `pipe-db-password`). Meldet sich mit `DefaultAzureCredential` an: Managed Identity in Azure, lokal das Entwickler-Login. |
| IdentityServer.NET | `identityServerVault`, Plugin `gView.Pipe.Plugins.Secrets` | `Authority`, `Locker`, `ClientId`, `ClientSecret`. |

Was ein Workflow erreicht, begrenzt der Server: der Präfix bei Umgebung und Key Vault, der Locker des Clients beim IdentityServer-Vault.

## Ein Secret verwenden

- **Credential:** Der Schlüssel-Knopf in einem Secret-Feld übernimmt den Wert aus einer Quelle. Nach dem Speichern erscheinen Quelle und Name statt des Werts.
- **Variable:** Der Schlüssel-Knopf im Wert einer Umgebung macht dasselbe.

In beiden Dialogen sagt *Check*, ob das Secret lesbar ist, ohne es zu zeigen. In der Datei steht die Referenz als Text `secret:<Quelle>/<Name>`, etwa `secret:vault/db-password`; mit `@3` hinter dem Namen wird eine Version gelesen (`secret:vault/db-password@3`), sonst die neueste. Die Quelle `env` kennt keine Versionen. Ein einfacher Wert darf nicht mit `secret:` beginnen.

Aus einer Quelle gelesene Werte werden in Logs, Node-Ausgaben und im Ausführungsverlauf maskiert wie geheime Variablen. Fehlt ein Secret oder ist die Quelle nicht erreichbar, schlägt der Lauf mit einer Meldung fehl, die Quelle und Secret nennt, nie den Wert.

## Vault von IdentityServer.NET einrichten

Der AppHost legt das nicht an; in der Admin-Oberfläche des IdentityServers:

1. Als System-Admin die API-Resource `secrets-vault` anlegen.
2. Einen Locker anlegen (zum Beispiel `pipe`) und die Secrets hineinlegen.
3. Einen API-Client mit Client-Secret und den Scopes `secrets-vault` und `secrets-vault.pipe` anlegen. Seine Daten gehören in die Quelle. Am IdentityServer muss `IdentityServer:PublicOrigin` gesetzt sein, sonst lehnt er jedes Token ab.

## Einstellungen des Hosts

Die Einstellungen der App selbst (`Pipe:Auth:Oidc:ClientSecret`, API-Keys, …) laufen nicht über diese Quellen. ASP.NET Core liest sie von sich aus aus Umgebungsvariablen oder über den Azure-Key-Vault-Konfigurationsprovider (`AddAzureKeyVault`).
