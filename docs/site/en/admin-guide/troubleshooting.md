# Troubleshooting

## The app doesn't start

- **Port in use:** another program uses the port. Change it with `--urls`.
- **Missing runtime:** `dotnet gView.Pipe.Shell.dll` needs the .NET 10 ASP.NET Core runtime (not needed for Docker).
- **No write access:** the account needs write access to the `data` folder.

## A node is missing in the palette

The plugin isn't installed or couldn't be loaded. Check:

1. The folder is `plugins/<Name>/` and contains `<Name>.dll` with exactly the folder's name.
2. The log at the start. A plugin that can't be loaded gives a warning that starts with `Plugins:`.
3. You restarted the app after copying the plugin.

## Script nodes don't run a script

- The script isn't in a released folder, or the folder isn't in `Pipe:Scripts:ScriptRoots`.
- The interpreter isn't installed (`python`, `pwsh`, `bash`). Set the path in `Pipe:Scripts:Interpreters`.
- Run Scripts Repo: `git` isn't installed, the Git server or its credential is wrong, or the repository filter hides the repository. The node shows hints about missing settings.
- In Docker: `.ps1`, `.bat` and `.cmd` scripts can't run in the Linux image.
- The node reports **failed** although the script looks fine: a non-zero exit code or a line that matches the error pattern fails it. Look at the log of the node for the output of the script.

## A webhook answers with an error

| Answer | Cause |
|---|---|
| `404` | The workflow isn't active, or the path is wrong. Webhook paths are lower case. |
| `405` | The method differs from the one set in the trigger. |
| `401` | The credential is missing or wrong. |
| `413` | The body is larger than 10 MB. |
| `429` | The workflow is already running and its limit of concurrent runs is reached. |
| `500` | The run failed. The body has the error and the `executionId`; look the run up in the history. See [Error handling](../user-guide/error-handling.md). |

If a workflow can't be activated because its path is already used, the editor shows which workflow and node use it.

## Credentials stopped working after a move or restore

The `data/keys/` folder doesn't match `data/credentials/`. Restore both from the same backup, or enter the credentials again.

## A workflow stops after a restart

Runs that were in progress end with a restart. Active workflows are armed again, but a Wait node that was waiting has lost its run.

## Where to look

- The application log (console, service log or `docker logs`).
- The execution history of the workflow: each node shows its input, output and log.
