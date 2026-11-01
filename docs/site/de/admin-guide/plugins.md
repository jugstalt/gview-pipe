# Plugins

Alle Nodes kommen aus Plugins. Die eingebauten Nodes (`gView.Pipe.Plugins.dll`) sind immer Teil der App. Zwei weitere Plugins sind getrennt, weil sie eigene Abhängigkeiten mitbringen oder Code ausführen können:

| Plugin | Nodes | Warum getrennt |
|---|---|---|
| `gView.Pipe.Plugins.Database` | SQL | Treiber für SQL Server, PostgreSQL und SQLite. |
| `gView.Pipe.Plugins.Scripts` | Run Script, Run Scripts Folder, Run Scripts Repo | Führt Skripte auf dem Server aus. Ohne dieses Plugin gibt es keine Skriptausführung. |

ZIP und Docker-Image enthalten beide. Entferne einen Plugin-Ordner, um es abzuschalten.

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

Der Lauf schlägt an diesem Node mit der Meldung `Unknown node type … Is the plugin installed?` fehl.
