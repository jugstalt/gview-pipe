# Konfiguration

Die Einstellungen stehen in `appsettings.json` neben der App (im Abschnitt `Pipe`). Überschreibe sie in `appsettings.Production.json`, mit Umgebungsvariablen oder auf der Kommandozeile. Die Startskripte im ZIP setzen die Ordner als Umgebungsvariablen.

## Einstellungen

| Schlüssel | Vorgabe | Bedeutung |
|---|---|---|
| `Pipe:DataPath` | `data` | Ordner für `workflows/`, `groups.json`, `executions/`, `credentials/` und die Data-Protection-`keys/`. |
| `Pipe:PluginPath` | `plugins` | Ordner mit externen Plugins, je Plugin ein Unterordner: `plugins/<Name>/<Name>.dll`. |
| `Pipe:FileRoots` | `files` → `files` | Ordner, die Read File und Write File benutzen dürfen, nach Namen. Außerhalb davon ist nichts erreichbar. |
| `Pipe:Scripts:ScriptRoots` | `scripts` → `scripts` | Skriptordner für die Skript-Nodes, nach Namen (siehe [Skripte ausführen](scripts.md)). |
| `Pipe:Scripts:GitServers` | keine | Git-Server für Run Scripts Repo. |
| `Pipe:Scripts:RepositoriesPath` | `data/script-repos` | Wohin Repositories für einen Lauf geklont werden. |
| `Pipe:Scripts:Interpreters` | keine | Interpreter je Dateiendung, zum Beispiel `.py` → `C:\Python313\python.exe`. |
| `Pipe:Scripts:GitPath` | `git` | Die git-Kommandozeile. |

Relative Pfade gelten relativ zum Arbeitsordner der App.

Beispiel (`appsettings.Production.json`):

```json
{
  "Pipe": {
    "DataPath": "D:\\gview-pipe\\data",
    "FileRoots": {
      "import": "D:\\exchange\\import",
      "export": "D:\\exchange\\export"
    }
  }
}
```

Ein Workflow nennt den Ordner (`import`) und einen Pfad darin. Er kann den Ordner nicht verlassen.

## Umgebungsvariablen

Für die Verschachtelung nimmst du zwei Unterstriche:

```
Pipe__DataPath=/var/lib/gview-pipe/data
Pipe__FileRoots__import=/srv/import
Pipe__Scripts__ScriptRoots__scripts=/srv/scripts
Pipe__Scripts__GitServers__company__Url=https://git.example.com
```

## Lauschadresse

Die Adresse setzt du mit `--urls` oder der Variablen `ASPNETCORE_URLS`, zum Beispiel `http://*:5100`. Das Docker-Image nutzt `http://*:8080`.

## Grenzen, die nicht in appsettings stehen

Einige Optionen lassen sich nur im Code setzen (`Program.cs` des Shell), zum Beispiel wie viele Executions je Workflow behalten werden (50), wie lange ein Wait-Node höchstens wartet (eine Stunde), die JavaScript-Grenzen und die maximale Body-Größe eines Webhooks (10 MB). Frag deinen Entwickler, wenn du sie ändern willst.

Pro Workflow stellst du die **maximale Zahl gleichzeitiger Läufe** (Vorgabe 1) im Workflow-Panel des Editors ein. Weitere gleichzeitige Läufe werden übersprungen, und ein Webhook-Aufrufer bekommt `429`.
