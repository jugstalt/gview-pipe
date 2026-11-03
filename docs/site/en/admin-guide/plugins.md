# Plugins

All nodes come from plugins. The built-in nodes (`gView.Pipe.Plugins.dll`) are always part of the app. Two further plugins are separate, because they bring their own dependencies or can run code:

| Plugin | Nodes | Why separate |
|---|---|---|
| `gView.Pipe.Plugins.Database` | SQL | SQL Server, PostgreSQL and SQLite drivers. |
| `gView.Pipe.Plugins.Office` | none; adds the format *Excel* to Extract from File and Convert to File | The Excel library (MiniExcel). |
| `gView.Pipe.Plugins.Scripts` | Run Script, Run Scripts Folder, Run Scripts Repo | Runs scripts on the server. Not installing it means no script execution. |
| `gView.Pipe.Plugins.Sftp` | SFTP | The SSH library (SSH.NET). |
| `gView.Pipe.Plugins.Secrets` | none; adds the secret sources Azure Key Vault and IdentityServer.NET (see [Secrets](secrets.md)) | The Azure SDK and the IdentityServer client. |

The ZIP and the Docker image contain all of them. Remove a plugin folder to switch it off.

## Folder layout

Each plugin has its own folder below `Pipe:PluginPath` (default `plugins`). The main DLL must have the name of the folder:

```
pipe\
  plugins\
    gView.Pipe.Plugins.Database\
      gView.Pipe.Plugins.Database.dll
      (its dependencies)
    gView.Pipe.Plugins.Scripts\
      gView.Pipe.Plugins.Scripts.dll
```

A plugin is loaded into its own assembly context, so its dependencies don't clash with those of the app. Restart the app after adding or removing a plugin.

## Third-party plugins

Copy the plugin folder into `plugins\`. Only install plugins from sources you trust: a plugin runs with the rights of the app and can do anything the app can do. How to write one is described in the [Developer Guide](../developer-guide/index.md).

## Workflows that use a missing plugin

The workflow still opens and the node keeps its settings, but running it fails at that node with `Unknown node type … Is the plugin installed?`. Install the plugin and the workflow works again.
