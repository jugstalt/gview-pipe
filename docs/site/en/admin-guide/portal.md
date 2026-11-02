# User portal

The portal is a small web app for people who **run** workflows but don't edit them. They see the workflows released for them in categories, fill in a form, wait for the result or let the run go on in the background, and stop their own runs.

The portal is a separate app (`gView.Pipe.Portal`). It has no engine and no tools; it calls the [portal endpoints](api.md#portal-endpoints) of the backend (the normal gView.Pipe app) with an API key. It can run on another server, for example on the internet, while the backend stays in the internal network:

```
Browser  →  portal server  →  backend (REST API)
```

The key stays on the portal server. The browser never talks to the backend and never sees the key.

## Set up the backend

1. On the page **API keys** create a key with the right **Portal** only. Copy it; it is shown once.
2. Let only the portal server reach the backend (a VPN, a firewall rule or a private network). A stolen key could otherwise act as any user.
3. Release workflows: in the [workflow settings](../user-guide/editor.md#workflow-settings) switch on *Publish in portal*. The group of the workflow is its category in the portal. Add a **Form Trigger** for the inputs and a **Result** node for what the user should see at the end.

## Configure the portal

Settings are read from `appsettings.json`, from `portal.config.json` next to the app and from the environment (`Portal__BackendUrl`).

| Setting | Meaning |
|---|---|
| `Portal:BackendUrl` | Address of the backend, e.g. `https://pipe.internal:7240`. Needed. |
| `Portal:ApiKey` | The key with the right *Portal*. Needed. Prefer the environment variable `Portal__ApiKey`. |
| `Portal:Title` | Name in the title bar (default `gView.Pipe`). |
| `Portal:PollSeconds` | Seconds between two status requests while a run goes on (default 2). |
| `Portal:Auth` | The login of the users, the same settings as [`Pipe:Auth`](authentication.md): `Mode` is `None`, `Forms` or `Oidc`. |

!!! warning
    With `Mode` *None* everybody who reaches the portal is the user *anonymous* and sees the runs of all anonymous users. Use a login whenever the portal can be reached by more than you.

The name of the signed-in user goes to the backend with every request. Runs show `portal:<name>` as the starter in the history of the editor, and users see and stop only their own runs.

Start it like the Shell: `dotnet run --project src/gView.Pipe.Portal` (port 5250 in development, `--launch-profile https` for https on 7250). In the AppHost the login is set at the top of `AppHost.cs`: `#define PORTAL_OIDC` (IdentityServer, user `portal@is.net`) or `#define PORTAL_FORMS` (fixed users `portal`, with the portal role `portal-demo` and an address, and `guest`, both with the name as password); define only one.

For the forms mode create the hash of a password with the portal itself:

```bash
dotnet gView.Pipe.Portal.dll --hash-password "my password"
```

and put it into `PasswordHash` of the user (see [Authentication](authentication.md#forms-fixed-users)). With OpenID Connect the client of the portal needs the redirect URIs `<portal address>/signin-oidc` and `<portal address>/signout-callback-oidc`. The portal needs https for it, behind a proxy with `Portal:Auth:UseForwardedHeaders`. It asks for the scopes `openid profile role email` unless you set `Portal:Auth:Oidc:Scopes`; `email` brings the address for the mail when a run is done, so the client must be allowed this scope.

## What users can do

- **Workflows**: all published workflows by category, with a search.
- **Form**: the fields of the Form Trigger. Fields with a pattern are checked while the user types; the backend checks every start again.
- **Wait or background**: the author sets the default and whether users may switch. A run the user waits for goes on in the background when the wait timeout has passed.
- **My runs**: the runs of the user with their status. Running runs have a *Stop* button, unless the workflow forbids it. Finished runs have *Run again*, which opens the form with the values of that run (passwords are never kept).
- **Result**: text, Markdown (without raw HTML) or a table, as defined by the Result node. Users never see raw items or the details of errors.

## Who sees which workflows

Without further settings every signed-in user sees all published workflows. To show some of them to some people only, use **portal roles**:

1. Give the users roles at the login of the portal. With OpenID Connect these are the claims of the role claim type of the provider (`Portal:Auth:Oidc:RoleClaimType`, default `role`). In the forms mode add `Roles` to the users in `Portal:Auth:Users`:

    ```json
    { "Name": "ada", "PasswordHash": "…", "Roles": [ "hr", "finance" ] }
    ```
2. In the editor name the roles that may see a workflow (*Only these roles* in its portal settings) or all published workflows of a group (*Portal roles* in the group dialog). The roles of a workflow count before those of its group. Without roles everybody sees it.

A user needs one of the roles; case is ignored. A workflow the roles don't allow is not listed, can't be started and its runs aren't shown. These roles are not the roles viewer to admin of the editor, and they are checked by the backend, not by the portal. Roles come from the login, so a change applies at the next sign-in.

## Mail when a run is done

A user can tick *Mail me when it is done* when starting a run. The mail is sent by a workflow that you build, so you decide its text and its SMTP server:

1. Create a workflow with an **Execute Workflow Trigger** and a **Send Email** node, e.g. *portal-mail*. The trigger gives it one item:

    ```json
    {
      "to": "ada@example.com",
      "user": "ada",
      "workflow": { "id": "invoice", "name": "Invoice" },
      "execution": { "id": "…", "status": "success", "startedAt": "…", "finishedAt": "…", "url": "https://portal.example.com/jobs/invoice/…" }
    }
    ```

    In the Send Email node use `{{ $json.to }}` as recipient, and for the text e.g. `Your run of {{ $json.workflow.name }} ended: {{ $json.execution.status }}. {{ $json.execution.url }}`. The item never holds the result of the run.
2. On the backend set `Pipe:Portal:NotifyWorkflowId` to its id, and `Pipe:Portal:PublicUrl` to the address of the portal, so `url` is filled.

The option appears only when the backend has a notification workflow and the user has an address: from the login (OIDC claim `email`) or, in the forms mode, the `Email` of the user in `Portal:Auth:Users` (the settings of the portal, not of the backend). No mail is sent when the user already got the result while waiting. A run that is still going when the backend restarts doesn't send its mail.

## Results and history

Users see their own runs with the values they entered and the result for `Pipe:Portal:HistoryDays` days (default 30); after that only the status. Values of password fields are not shown. The history of the editor keeps the data of the run as usual, so switch off *Save item data of executions* for workflows with secrets.

## Limits

A user may have `Pipe:Portal:MaxRunsPerUser` runs at the same time on the backend (default 3), and a workflow can allow only one run at a time. Beyond that the portal shows the message of the backend.
