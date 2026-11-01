# Configuration

The settings are in `_config/pipe.config` in the working directory of the app (in the `Pipe` section, JSON with comments). If the file is missing at the start, the app creates it from the prototype `_setup/proto/pipe.config`; adjust it afterwards. `appsettings.json` holds only ASP.NET Core settings like `Logging`. Environment variables and the command line override `pipe.config`. The start scripts of the ZIP set the folders as environment variables.

In the Docker image `/app/_config` is a volume, so the file survives updates.

## Keeping the configuration over updates

The variable `PIPE_CONFIG_PATH` moves the `_config` folder. The start scripts and `gview-pipe-service.bat install` set it to `<root>\_config`, next to the version folders, so every installation (root folder) has its own `pipe.config` and keeps it over updates. On the first start the app creates it there from the prototype. Edit `<root>\_config\pipe.config`; the version folder has no `_config` of its own.

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
| `Pipe:Environment` | `DEFAULT` | The environment this instance runs in. |
| `Pipe:Environments` | `TEST`, `STAGE`, `PROD` | Environments groups and workflows can choose, besides `DEFAULT`. |
| `Pipe:Variables` | none | Instance variables per environment. |
| `Pipe:SecretVariables` | none | Names of instance variables that are masked in logs and script output. |

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

## Environments and variables

Each instance runs in one **environment**: a test server in `TEST`, the production server in `PROD`. Groups and workflows inherit it unless they set their own, see [Variables and environments](../user-guide/variables.md). Users can't change the environment of the instance in the app.

```json
{
  "Pipe": {
    "Environment": "PROD",
    "Environments": [ "TEST", "STAGE", "PROD" ],
    "Variables": {
      "DATABASE_SERVER": { "DEFAULT": "db-test", "PROD": "db-prod" },
      "DATABASE_PASSWORD": { "PROD": "..." }
    },
    "SecretVariables": [ "DATABASE_PASSWORD" ]
  }
}
```

Variables of the configuration are the lowest level. Values entered on the **Variables** page of the app win over them, per environment. The app stores those in `variables.json` in the data folder; secret values are encrypted with the keys in `keys/`. Secret values of groups and workflows are encrypted the same way, so a workflow file copied to another installation loses them.

## Environment variables

Use two underscores for the nesting:

```
Pipe__DataPath=/var/lib/gview-pipe/data
Pipe__FileRoots__import=/srv/import
Pipe__Scripts__ScriptRoots__scripts=/srv/scripts
Pipe__Scripts__GitServers__company__Url=https://git.example.com
Pipe__Environment=PROD
Pipe__Variables__DATABASE_SERVER__PROD=db-prod
```

## Listening address

The start scripts listen on `http://localhost:5100` by default. To change it, for example for a second instance on the same machine, set `urls` at the top of `pipe.config` (next to `Pipe`, not inside it):

```json
{
  "urls": "http://localhost:5101",
  "Pipe": { ... }
}
```

`urls` in `pipe.config` wins over `ASPNETCORE_URLS`; `--urls` on the command line wins over both. The Windows service passes its port with `--urls` (`gview-pipe-service.bat install <name> <port>`). The Docker image uses `http://*:8080`.

## Limits that are not in the configuration

A few options can only be set in code (`Program.cs` of the Shell), for example how many executions are kept per workflow (50), how long a Wait node may wait (one hour), the JavaScript limits and the maximum body size of a webhook (10 MB). Ask your developer if you need to change them.

Per workflow you can set the **maximum concurrent runs** (default 1) in the workflow panel of the editor. More runs at the same time are skipped, and a webhook caller gets `429`.
