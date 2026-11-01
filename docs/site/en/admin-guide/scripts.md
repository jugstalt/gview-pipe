# Running scripts

The script nodes run existing scripts (`.bat`, `.cmd`, `.ps1`, `.py`, `.sh`) from workflows. Running code on the server is risky by nature, so **you** decide what may run: workflows can't type in a path or a command. They can only pick files from sources you release.

## Prerequisites

Install the plugin `gView.Pipe.Plugins.Scripts` (see [Plugins](plugins.md)). Without it, the nodes don't exist.

The interpreter is chosen by the file extension. Only what the operating system can run is available:

| Extension | Interpreter |
|---|---|
| `.bat`, `.cmd` | `cmd.exe` (Windows) |
| `.ps1` | `pwsh`, otherwise Windows PowerShell |
| `.py` | `python` |
| `.sh` | `bash` |

Change an interpreter with `Pipe:Scripts:Interpreters`, for example `".py": "C:\\Python313\\python.exe"`.

## Script folders

A script folder is a folder on the server that you release by name:

```json
"Pipe": {
  "Scripts": {
    "ScriptRoots": { "scripts": "D:\\ops\\scripts", "maintenance": "D:\\ops\\maintenance" }
  }
}
```

The nodes **Run Script** and **Run Scripts Folder** list the files of these folders, including subfolders.

!!! warning
    Keep script folders apart from the folders you release for Write File (`Pipe:FileRoots`). Otherwise a workflow could write a script and then run it.

## Git servers

**Run Scripts Repo** takes scripts from a repository of a Git server you configure. Gitea and GitHub are supported:

```json
"Pipe": {
  "Scripts": {
    "GitServers": {
      "company": {
        "Provider": "gitea",
        "Url": "https://git.example.com",
        "Owner": "ops",
        "CredentialId": "git-token",
        "RepositoryFilter": "^ops-"
      }
    }
  }
}
```

| Setting | Meaning |
|---|---|
| `Provider` | `gitea` or `github`. Decides which API lists repositories, branches and files. |
| `Url` | Base URL, for example `https://github.com`. |
| `Owner` | The organization or user whose repositories are offered. |
| `CredentialId` | The id of a Pipe credential (Basic Auth with user and password or token, or Bearer Token). Create it in the credentials list first. |
| `RepositoryFilter` | Optional regular expression; only repositories whose name matches are offered. |

Each run makes a shallow clone (`git clone --depth 1`) into a temporary folder below `Pipe:Scripts:RepositoriesPath`, runs the scripts there and deletes the folder afterwards, also after a failure or cancel. Leftovers are removed when the app starts. There is never a stale checkout. The credential reaches git as an HTTP header in the environment, never in the URL or in a log. Only HTTPS (or local paths) works, no SSH.

The **git command line must be installed** on the server (it is part of the Docker image).

## How a script runs

- One process per script, with the script's folder (the repository root for Run Scripts Repo) as working directory.
- The environment contains the environment of the app plus `PIPE_ITEM`, `PIPE_WORKFLOW`, `PIPE_EXECUTION`, the variables of the node and, if the node has a credential, `PIPE_CREDENTIAL_<FIELD>`. Credential values are masked in the output and in logs.
- The default timeout is 10 minutes per script. A timeout or a cancel ends the whole process tree.
- The output is limited to 1 MB per stream and is also written to the log of the node.
- A script **fails** when its exit code is not 0, or when a line of its output matches the error pattern of the node (a regular expression).

How a failing script affects the workflow is described in [Error handling](../user-guide/error-handling.md).

## Security

Scripts run with the **rights of the app's account**. Run the app under an account that may do only what the scripts need. See [Security](security.md).
