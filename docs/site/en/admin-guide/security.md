# Security

This page lists what the app protects and what you must protect yourself.

## Login

Without configuration (`Pipe:Auth:Mode` = `none`), the editor and all its functions are open to everyone who can reach the address. Then:

- bind the app to `localhost` or an internal network, and
- put a reverse proxy or a VPN in front of it if people connect from other machines.

As soon as other people can reach the app, switch on a login: fixed users (`forms`) or an identity provider (`oidc`), see [Authentication](authentication.md). Everyone who is signed in may do everything; there are no roles inside the app. Serve the app over https when it has a login, so passwords and the session cookie are encrypted.

## What a workflow can do

A workflow can call web addresses (HTTP Request), send mail, read and write files, run SQL and, with the script plugin, run scripts. You limit that by what you release:

| Capability | Limited by |
|---|---|
| Read File / Write File | Only the folders in `Pipe:FileRoots`, addressed by name. |
| Scripts | Only files from `Pipe:Scripts:ScriptRoots` or from the configured Git servers. No inline scripts, no free paths. |
| SQL | Only through a Connection String credential. Values are always parameters; the SQL text is never evaluated as an expression. |
| JavaScript (expressions, Code node) | A sandboxed engine with time and memory limits. |

Whoever can edit workflows can use everything you release. Treat editor access as **administrator access** to the released folders, servers and credentials.

## Scripts run with the account of the app

Run the app under an account (or container) that may do only what your scripts need. Don't run it as an administrator or root.

## Credentials

- Credentials are stored encrypted with ASP.NET Data Protection. Secret fields are never shown again after saving.
- Nodes reference credentials by id. They are not part of the workflow JSON, so you can share workflow files.
- The keys are stored in `data/keys/`. **Anyone with access to both `keys/` and `credentials/` can decrypt the credentials.** Restrict access to the `data` folder. For production, a developer can configure Data Protection to protect the keys with DPAPI, a certificate or a key vault.
- The headers `authorization`, `proxy-authorization` and `cookie` of webhook calls (and the API key header) are not copied into the items and so never reach the execution history. Credential values are masked in script output and logs.

Values can also stay outside `data/`: in environment variables, Azure Key Vault or the vault of IdentityServer.NET, see [Secrets](secrets.md).

## Webhooks

Give every webhook that is reachable from outside a credential. See [Operations](operations.md#webhooks-in-production).

## REST API

The [REST API](api.md) under `/api/v1` accepts only API keys, never the login cookie of the UI.

- Give each script or pipeline its own key with only the rights it needs, limited to its groups and with an expiry date. Delete keys that are no longer used; the page shows when each was last used.
- Only the SHA-256 of a key is stored in `data/api-keys.json`; a copy of the data folder doesn't reveal keys.
- Only admins create keys ([roles](authentication.md#roles)). Without a login everyone is admin: use one when the UI is reachable by others.
- Send keys only over HTTPS.

## Plugins

A plugin runs with the full rights of the app. Install only plugins from sources you trust.

## Audit log

The [audit log](operations.md#audit-log) records who changed, deleted or ran what, sign-ins and refused attempts. It never contains values of credentials or secret variables. For proof that can't be changed afterwards, forward the application log (category `gView.Pipe.Audit`) to a central log system.

## Updates

Install updates regularly. The runtime and the third-party libraries receive security fixes.
