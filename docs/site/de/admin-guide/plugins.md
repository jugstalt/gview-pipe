# Plugins

Alle Nodes kommen aus Plugins. Die eingebauten Nodes (`gView.Pipe.Plugins.dll`) sind immer Teil der App. Die anderen Plugins sind getrennt, weil sie eigene Abhängigkeiten mitbringen oder Code ausführen können:

| Plugin | Nodes | Warum getrennt |
|---|---|---|
| `gView.Pipe.Plugins.AI` | AI | Schickt Daten der Items an ein Sprachmodell (Anthropic API oder Azure OpenAI). Ohne dieses Plugin gehen keine Daten an ein Modell. |
| `gView.Pipe.Plugins.AzureStorage` | Azure Blob, Azure Blob Trigger | Das Azure Storage SDK. |
| `gView.Pipe.Plugins.Database` | SQL | Treiber für SQL Server, PostgreSQL und SQLite. |
| `gView.Pipe.Plugins.Office` | keine; ergänzt das Format *Excel* in Extract from File und Convert to File | Die Excel-Bibliothek (MiniExcel). |
| `gView.Pipe.Plugins.Scripts` | Run Script, Run Scripts Folder, Run Scripts Repo | Führt Skripte auf dem Server aus. Ohne dieses Plugin gibt es keine Skriptausführung. |
| `gView.Pipe.Plugins.Sftp` | SFTP | Die SSH-Bibliothek (SSH.NET). |
| `gView.Pipe.Plugins.Secrets` | keine; ergänzt die Secret-Quellen Azure Key Vault und IdentityServer.NET (siehe [Secrets](secrets.md)) | Das Azure SDK und der IdentityServer-Client. |

ZIP und Docker-Image enthalten alle. Entferne einen Plugin-Ordner, um es abzuschalten.

## Ordnerstruktur

Jedes Plugin hat einen eigenen Ordner unterhalb von `Pipe:PluginPath` (Vorgabe `plugins`). Die Haupt-DLL muss den Namen des Ordners tragen:

```
pipe\
  plugins\
    gView.Pipe.Plugins.Database\
      gView.Pipe.Plugins.Database.dll
      (seine Abhängigkeiten)
    gView.Pipe.Plugins.Scripts\
      gView.Pipe.Plugins.Scripts.dll
```

Ein Plugin wird in einen eigenen Assembly-Kontext geladen, seine Abhängigkeiten kollidieren also nicht mit denen der App. Starte die App neu, nachdem du ein Plugin hinzugefügt oder entfernt hast.

## Plugins von Dritten

Kopiere den Plugin-Ordner nach `plugins\`. Installiere nur Plugins aus Quellen, denen du vertraust: Ein Plugin läuft mit den Rechten der App und kann alles tun, was die App kann. Wie man eines schreibt, steht im [Entwicklerhandbuch](../developer-guide/index.md) (nur auf Englisch).

## Workflows, die ein fehlendes Plugin benutzen

Der Workflow lässt sich weiter öffnen, und der Node behält seine Einstellungen, aber der Lauf schlägt an diesem Node mit `Unknown node type … Is the plugin installed?` fehl. Installiere das Plugin, dann funktioniert der Workflow wieder.
