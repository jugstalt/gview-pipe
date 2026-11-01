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
- In the ZIP the working directory is the version folder, so `_config` belongs to a version; the start scripts keep setting the folders as environment variables.
