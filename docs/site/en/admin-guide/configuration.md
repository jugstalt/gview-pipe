# Configuration

The settings are in `_config/pipe.config` in the working directory of the app (in the `Pipe` section, JSON with comments). If the file is missing at the start, the app creates it from the prototype `_setup/proto/pipe.config`; adjust it afterwards. `appsettings.json` holds only ASP.NET Core settings like `Logging`. Environment variables and the command line override `pipe.config`. The start scripts of the ZIP set the folders as environment variables.

In the Docker image `/app/_config` is a volume, so the file survives updates.

## Settings

| Key | Default | Meaning |
|---|---|---|
| `Pipe:DataPath` | `data` | Folder for `workflows/`, `groups.json`, `executions/`, `credentials/` and the Data Protection `keys/`. |
| `Pipe:DocsPath` | `docs` | Folder with the built documentation, served under `/docs`. The ZIP and the Docker image contain it. Without it there is no `/docs` and no Docs link. |
| `Pipe:PluginPath` | `plugins` | Folder with external plugins, one sub folder per plugin: `plugins/<Name>/<Name>.dll`. |
| `Pipe:FileRoots` | `files` → `files` | Folders that Read File and Write File may use, by name. Nothing outside them is reachable. |
| `Pipe:Scripts:ScriptRoots` | `scripts` → `scripts` | Script folders for the script nodes, by name (see [Running scripts](scripts.md)). |
| `Pipe:Scripts:GitServers` | none | Git servers for Run Scripts Repo. |
| `Pipe:Scripts:RepositoriesPath` | `data/script-repos` | Where repositories are cloned for a run. |
| `Pipe:Scripts:Interpreters` | none | Interpreter per file extension, for example `.py` → `C:\Python313\python.exe`. |
| `Pipe:Scripts:GitPath` | `git` | The git command line. |

Relative paths are resolved against the working directory of the app.

Example (`_config/pipe.config`):

```json
{
  "Pipe": {
    "DataPath": "D:\\gview-pipe\\data",
    "FileRoots": {
      "import": "D:\\exchange\\import",
      "export": "D:\\exchange\\export"
    }
  }
}
```

A workflow names the folder (`import`) and a path inside it. It can't leave the folder.

## Environment variables

Use two underscores for the nesting:

```
Pipe__DataPath=/var/lib/gview-pipe/data
Pipe__FileRoots__import=/srv/import
Pipe__Scripts__ScriptRoots__scripts=/srv/scripts
Pipe__Scripts__GitServers__company__Url=https://git.example.com
```

## Listening address

Set the address with `--urls` or the `ASPNETCORE_URLS` variable, for example `http://*:5100`. The Docker image uses `http://*:8080`.

## Limits that are not in the configuration

A few options can only be set in code (`Program.cs` of the Shell), for example how many executions are kept per workflow (50), how long a Wait node may wait (one hour), the JavaScript limits and the maximum body size of a webhook (10 MB). Ask your developer if you need to change them.

Per workflow you can set the **maximum concurrent runs** (default 1) in the workflow panel of the editor. More runs at the same time are skipped, and a webhook caller gets `429`.
