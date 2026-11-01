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
    The start script binds to `localhost` only. To reach the app from other machines, put a reverse proxy in front of it (see [Operations](operations.md#running-behind-a-reverse-proxy)) or set `urls` in `<root>\_config\pipe.config` (see [Configuration](configuration.md#listening-address)). There is **no login** yet: don't expose the app directly to a network you don't trust (see [Security](security.md)).

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

The start script runs the app only as long as you are logged on. To keep it running on a server, run it as a Windows service, in IIS, as a systemd unit or in Docker. In every case, run it under an account with **restricted rights**, because scripts run with the rights of that account (see [Security](security.md)).

Active workflows with a Cron or Webhook trigger are armed again when the app starts.

### Windows service

The version folder contains `gview-pipe-service.bat`. Run it in a command prompt **as administrator**:

```
gview-pipe-service.bat install [name] [port]
gview-pipe-service.bat uninstall [name]
```

`install` creates the service and starts it:

- Service name `gview-pipe`, or `gview-pipe-<name>` with a name. It starts automatically (delayed) and is restarted after a crash.
- It listens on `http://localhost:<port>` (default `5100`), like the start script.
- It runs as the virtual account `NT SERVICE\gview-pipe[-<name>]`, which may write only to `data`, `files`, `scripts` and `_config`.
- `data`, `files`, `scripts` and `_config` next to the version folders are set as environment variables of the service, like in the start script.

**Several instances** on one server, for example production and test: unpack the ZIP into one root folder per instance and give each a name and a port.

```
D:\gview-pipe-prod\0.1.0\gview-pipe-service.bat install prod 5100
D:\gview-pipe-test\0.1.0\gview-pipe-service.bat install test 5200
```

`install` refuses a name whose service belongs to another root folder.

**Update:** unpack the new version into the same root folder and run its `gview-pipe-service.bat install` with the same name and port. The script stops the service, points it to the new version and starts it again. To go back, run `install` of the old version.

**Own account:** for network shares or Git with Windows authentication, the virtual account is not enough. Enter a domain or local account in the service properties (tab **Log On**) and give it write access to the same folders.

The log goes to the Windows event log (application log, source `gView.Pipe`, warnings and errors).

### IIS

gView.Pipe runs in IIS like any ASP.NET Core app:

- Install the **ASP.NET Core Hosting Bundle** for .NET 10.
- Create a site or application whose physical path is the `pipe` folder of the version.
- Set the application pool to **No Managed Code** and give its identity write access to `data`, `files`, `scripts` and `_config`.

The start script does not run in IIS. Set `PIPE_CONFIG_PATH=<root>\_config` as an environment variable of the application pool (not in the `web.config` of the version, which an update replaces), and set the folders as absolute paths in `<root>\_config\pipe.config` (see [Configuration](configuration.md#keeping-the-configuration-over-updates)).

### Linux and Docker

Use a systemd unit that runs `gview-pipe.sh` or `dotnet gView.Pipe.Shell.dll` with the same environment variables, or a Docker restart policy (`--restart unless-stopped`).

## Documentation

The app serves this documentation itself under `/docs` (for example `http://localhost:5100/docs`), in the language of the browser (English or German). The **Docs** link in the top bar opens it. It works offline and always matches the installed version.

## Check the installation

- The start page opens and shows the sample workflows.
- The log at the start says `Plugins: <n> node types from <m> assemblies`. If a plugin could not be loaded, a warning follows (see [Troubleshooting](troubleshooting.md)).
