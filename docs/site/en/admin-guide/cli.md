# Command line

`gview-pipe` is a command line client of the [REST API](api.md) for scripts and CI pipelines: deploy workflows from files, start runs and wait for them, look at executions.

## Install

```bash
dotnet tool install --global gView.Pipe.Cli
gview-pipe --help
```

This works once the package is published on NuGet. Until then build it from the source and install it from the folder:

```bash
dotnet pack src/gView.Pipe.Cli -c Release -o artifacts/cli
dotnet tool install --global gView.Pipe.Cli --add-source artifacts/cli
```

After a change, pack again and run `dotnet tool update --global gView.Pipe.Cli --add-source artifacts/cli` (raise the version in `Directory.Build.props` first, otherwise the cached package is used). `dotnet tool uninstall --global gView.Pipe.Cli` removes it.

It needs the .NET 10 runtime and an [API key](api.md#api-keys) with the rights for what you do (read, run, deploy).

## Connect

Set the address of the server and the key as environment variables, so the key doesn't appear in the process list or in logs of the pipeline:

=== "bash"

    ```bash
    export PIPE_URL=https://pipe.example.com
    export PIPE_API_KEY=pipe_...
    ```

=== "PowerShell"

    ```powershell
    $env:PIPE_URL = "https://pipe.example.com"
    $env:PIPE_API_KEY = "pipe_..."
    ```

`--url` and `--key` do the same for one call.

## Commands

| Command | Does |
|---|---|
| `gview-pipe workflows list [--json]` | Lists the workflows. |
| `gview-pipe workflows get <id>` | Prints the workflow as JSON. |
| `gview-pipe workflows export <id> --out <file or folder>` | Saves the workflow as a file; with a folder it is named `<id>.json`. |
| `gview-pipe workflows deploy <file or folder>... [--dry-run] [--force]` | Creates or replaces workflows from JSON files (folders: all `*.json` below). |
| `gview-pipe workflows delete <id>` | Deletes a workflow. |
| `gview-pipe workflows activate <id>`, `deactivate <id>` | Switches its triggers on or off. |
| `gview-pipe run <id> [--wait] [--input <file or ->] [--environment <name>] [--timeout <seconds>] [--json]` | Starts a run. |
| `gview-pipe executions list [--workflow <id>] [--status <status>] [--limit <n>] [--json]` | Lists executions, newest first. |
| `gview-pipe executions get <workflowId> <id>` | One execution with its node runs. |
| `gview-pipe executions cancel <workflowId> <id>` | Stops a running or queued run. |
| `gview-pipe executions replay <workflowId> <id> [--from <nodeId>]` | Runs an execution again. |

## Deploying

```bash
gview-pipe workflows deploy workflows/            # every *.json below the folder
gview-pipe workflows deploy hello-pipe.json --dry-run
```

The file is the JSON of a workflow, as `gview-pipe workflows export` writes it or as stored in `data/workflows`; the `id` inside names the workflow. The server checks it before saving; problems are listed and the exit code is 2.

- A workflow that exists is only replaced if nobody changed it since the command read it, for example in the editor. Otherwise the command stops with a message; `--force` replaces it anyway.
- `--dry-run` only reports *would create* or *would update*. It doesn't check the workflow; the server does that on the real deploy.
- `deploy` never deletes. Remove workflows one by one with `gview-pipe workflows delete`.
- Credentials that don't exist on the server are reported as warnings.

## Running

```bash
gview-pipe run hello-pipe                                   # starts it, prints the execution id
gview-pipe run hello-pipe --wait --input item.json          # waits and prints the items of the last node
echo '{ "name": "Ada" }' | gview-pipe run hello-pipe --wait --input -
```

`--input` is a JSON object (one item) or an array of objects; `-` reads it from standard input. It starts like a triggered run, see [Running a workflow](api.md#running-a-workflow).

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Done. Without `--wait` a started run counts as done. |
| 1 | `gview-pipe run --wait`: the run ended with the status `error`. |
| 2 | Wrong call, or the server refused it (`401` key, `404`, `422` invalid workflow, `429` queue full). |

A pipeline step can therefore use the command directly: a failed run or a refused deploy fails the step.

```yaml
# example: GitHub Actions
- run: dotnet tool install --global gView.Pipe.Cli
- run: gview-pipe workflows deploy workflows/
  env:
    PIPE_URL: ${{ vars.PIPE_URL }}
    PIPE_API_KEY: ${{ secrets.PIPE_API_KEY }}
```
