# Installation

gView.Pipe needs the .NET 10 runtime (ASP.NET Core). The Docker image brings its own.

## ZIP (Windows and Linux)

Download `gview-pipe-win64-<version>.zip` or `gview-pipe-linux64-<version>.zip` and unpack it into a folder of your choice, for example `C:\gview-pipe`. The ZIP contains one folder per version:

```
gview-pipe\
  0.1.0\
    pipe\                 the app and its plugins
    gview-pipe.bat        start script (gview-pipe.sh on Linux)
  data\                   created at the first start: workflows, executions, credentials, keys
  files\                  folder for Read File / Write File
  scripts\                folder for Run Script
```

`data`, `files` and `scripts` live **next to** the version folders, so they survive an update. The start script sets their location for you.

Start the app with the start script. It listens on `http://localhost:5100`. Open that address in a browser.

!!! note
    The start script binds to `localhost` only. To reach the app from other machines, put a reverse proxy in front of it (see [Operations](operations.md#running-behind-a-reverse-proxy)) or change `--urls` in the script. There is **no login** yet: don't expose the app directly to a network you don't trust (see [Security](security.md)).

On the first start with an empty `data` folder, gView.Pipe copies the sample workflows into the group **Samples**.

## Docker

```bash
docker run -d --name gview-pipe -p 8080:8080 \
  -v gview-pipe-data:/app/data \
  ghcr.io/jugstalt/gview-pipe:latest
```

- The app listens on port 8080 inside the container.
- `/app/data` holds all runtime state: mount a volume or a folder, or the data is lost with the container.
- The image contains `git` (for Run Scripts Repo) and `python3` (for `.py` scripts). It has no PowerShell and no Windows shell, so `.ps1`, `.bat` and `.cmd` scripts don't run in it.
- For Read File / Write File and script folders, mount the folders and name them in the configuration, for example `-v /srv/pipe/files:/app/files -v /srv/pipe/scripts:/app/scripts`. The defaults are `files` and `scripts` below `/app`.

Settings are passed as environment variables, see [Configuration](configuration.md#environment-variables).

## Starting automatically

gView.Pipe is a normal ASP.NET Core app (`dotnet gView.Pipe.Shell.dll`). To keep it running, use what your platform offers: a Windows service (for example with NSSM or `sc.exe`), a systemd unit, or a Docker restart policy (`--restart unless-stopped`). Run it under an account with **restricted rights**, because scripts run with the rights of that account (see [Security](security.md)).

Active workflows with a Cron or Webhook trigger are armed again when the app starts.

## Documentation

The app serves this documentation itself under `/docs` (for example `http://localhost:5100/docs`), in the language of the browser (English or German). The **Docs** link in the top bar opens it. It works offline and always matches the installed version.

## Check the installation

- The start page opens and shows the sample workflows.
- The log at the start says `Plugins: <n> node types from <m> assemblies`. If a plugin could not be loaded, a warning follows (see [Troubleshooting](troubleshooting.md)).
