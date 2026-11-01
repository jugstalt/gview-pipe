# 12. Configuration in _config

Date: 2026-11-01 · Status: accepted

## Context

The settings of the Shell (`Pipe:*`) were in `appsettings.json` and `appsettings.Development.json`, which are checked in. Local values (folders, Git servers) ended up in tracked files. The sibling apps of gview-gis keep their configuration elsewhere.

## Decision

- As in gview-gis (gView.Server, gView.WebApps), the app configuration lives in `_config/pipe.config`, relative to the working directory. The folder is ignored by Git.
- `appsettings*.json` hold only ASP.NET Core settings such as `Logging` and `AllowedHosts`.
- The checked-in prototype `_setup/proto/pipe.config` is copied 1:1 to `_config/pipe.config` on start when that file is missing (`PipeConfigSetup`, Shell only). No placeholders: paths are relative, and deployments override them with environment variables. Without a prototype the app starts with the code defaults and a warning.
- The file keeps the `Pipe` section, so keys, node hints and `Pipe__…` variables don't change.
- Order of the sources: appsettings → `_config/pipe.config` → environment variables → command line.
- The Docker image declares `/app/_config` as a volume.

## Consequences

- Embedding hosts are not affected; they configure `PipeCoreOptions` themselves.
- In the ZIP the working directory is the version folder, so `_config` would belong to a version; the start scripts keep setting the folders as environment variables.

## Addendum: `PIPE_CONFIG_PATH` (2026-11-01)

- The variable `PIPE_CONFIG_PATH` replaces the folder `_config`; the prototype is still read from the app folder. The start scripts and the Windows service set it to `<root>/_config`, next to the version folders.
- Copying a `<root>/_config/pipe.config` into the version on every start was dropped: edits of the copy were overwritten, and a file created from the prototype in the version folder was lost with the next update.
- The address moves out of the start scripts: they set the default with `ASPNETCORE_URLS`, which ranks below `pipe.config`, so a `urls` there (one port per instance) survives updates. The prototype has `urls` only as a comment, so development, Aspire and Docker keep their own addresses.
