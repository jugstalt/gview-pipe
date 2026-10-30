# 11. Running scripts

Date: 2026-10-30 · Status: accepted

## Context

Operations work often lives in existing scripts (batch files, PowerShell, Python). Workflows should be able to run them, on a schedule or after other steps, and react to their result. Running code on the server is risky by nature; the design keeps the decision about what may run with the host.

## Decision

- **External plugin** `gView.Pipe.Plugins.Scripts` with three nodes. Not installing the plugin means no script execution. There are no inline scripts: only files from sources the host released run.
  - **Run Script**: one file from a script folder (subfolders included), per input item.
  - **Run Scripts Folder**: a list of scripts from a folder or file of a script folder (the original Run Scripts id, Version 2).
  - **Run Scripts Repo**: a list of scripts from a repository of a configured Git server.
- **Sources**, released by the host in `Pipe:Scripts` (`PipeScriptsOptions`), never free paths or URLs:
  - **Script folders** (`ScriptRoots`), separate from the file roots of Read/Write File, so a workflow can't write a script and then run it.
  - **Git servers** (`GitServers`: name → `Provider` gitea/github, `Url`, `Owner` (organization or user), `CredentialId` of a Pipe credential, optional `RepositoryFilter` regex). The editor lists the owner's repositories, their branches (empty = default branch) and the files of the chosen branch through the provider's API, without a clone.
  - Each run of Run Scripts Repo makes a shallow clone (`git clone --depth 1`, installed **git command line**) into `{RepositoriesPath}/{guid}`, runs there with the repository root as working directory and `PIPE_WORK_DIRECTORY`, and deletes the folder afterwards, also on failure or cancel; leftovers are removed at host start. No fallback to an earlier checkout. Results carry the commit hash. Credentials (Basic Auth, or a token as Bearer for the API and `x-access-token` for git) reach git as an HTTP header in environment variables, never in the URL, the folder or a log. Only HTTPS (or local paths); no SSH.
  - Migration: version 1 Run Scripts nodes with a folder source keep working; those with a git source fail with a message to use Run Scripts Repo.
- **Interpreter by extension**, only what the host OS can run: `.bat`/`.cmd` (cmd.exe), `.ps1` (pwsh, else Windows PowerShell), `.py` (python), `.sh` (bash); overridable per extension.
- **Script list in the node:** a new editor contract, `IPipeParameterOptions` with `[PipeOptions]`, lets a node offer values that depend on its other parameters. The scripts are stored as an ordered `List<PipeSelection>` (value + enabled). The editor merges it with the files found: files added later appear at the end, switched off; stored files that are gone are marked missing. The first time, a single file is on, several are off.
- **Execution:** per input item the enabled scripts run one after the other, each in its own process, with the script's folder (repository root for Run Scripts Repo) as working directory, arguments with expressions, the host environment plus `PIPE_ITEM`, `PIPE_WORKFLOW`, `PIPE_EXECUTION`, own variables and optionally a credential as `PIPE_CREDENTIAL_<FIELD>` (masked in output and logs). Timeout per script (default 10 minutes) and cancel end the whole process tree. Output is limited to 1 MB per stream and also written to the node's log (stderr as warnings).
- **Result:** one item per script `{ script, success, exitCode, reason, matchedLine, stdout, stderr, durationMs, commit? }`. A script fails on a non-zero exit code or when an output line matches the error pattern (regex). A failing script fails the node ("Stop on first failure" by default); the node throws a `PipeNodeException` carrying the results, so `OnError = Continue` or `ErrorOutput` passes the script results on instead of the input items.

## Consequences

- Scripts run with the rights of the host process; run the service under a restricted account.
- git must be installed for repository sources; the plugin says so in its hints. A later switch to a library (e.g. for SSH) only touches `PipeGitServers`.
- A clone per run costs time and traffic but never runs stale or half-updated files.
- `IPipeParameterOptions` and `PipeNodeException` are general: other nodes can offer server-side choices and keep results on failure.
