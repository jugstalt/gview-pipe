# Konfiguration

Die Einstellungen stehen in `_config/pipe.config` im Arbeitsordner der App (im Abschnitt `Pipe`, JSON mit Kommentaren). Fehlt die Datei beim Start, legt die App sie aus der Vorlage `_setup/proto/pipe.config` an; danach passt du sie an. `appsettings.json` enthält nur ASP.NET-Core-Einstellungen wie `Logging`. Umgebungsvariablen und die Kommandozeile überschreiben `pipe.config`. Die Startskripte im ZIP setzen die Ordner als Umgebungsvariablen.

Im Docker-Image ist `/app/_config` ein Volume, damit die Datei Updates übersteht.

## Konfiguration über Updates behalten

Die Variable `PIPE_CONFIG_PATH` verlegt den Ordner `_config`. Die Startskripte und `gview-pipe-service.bat install` setzen sie auf `<root>\_config`, neben die Versionsordner. So hat jede Installation (jeder Root-Ordner) ihre eigene `pipe.config` und behält sie über Updates. Beim ersten Start legt die App sie dort aus der Vorlage an. Bearbeite `<root>\_config\pipe.config`; der Versionsordner hat kein eigenes `_config`.

## Einstellungen

| Schlüssel | Vorgabe | Bedeutung |
|---|---|---|
| `Pipe:DataPath` | `data` | Ordner für `workflows/`, `groups.json`, `executions/`, `credentials/` und die Data-Protection-`keys/`. |
| `Pipe:DocsPath` | `docs` | Ordner mit der gebauten Dokumentation, die unter `/docs` ausgeliefert wird. ZIP und Docker-Image enthalten sie. Ohne ihn gibt es weder `/docs` noch den Docs-Link. |
| `Pipe:PluginPath` | `plugins` | Ordner mit externen Plugins, je Plugin ein Unterordner: `plugins/<Name>/<Name>.dll`. |
| `Pipe:FileRoots` | `files` → `files` | Ordner, die Read File und Write File benutzen dürfen, nach Namen. Außerhalb davon ist nichts erreichbar. |
| `Pipe:Scripts:ScriptRoots` | `scripts` → `scripts` | Skriptordner für die Skript-Nodes, nach Namen (siehe [Skripte ausführen](scripts.md)). |
| `Pipe:Scripts:GitServers` | keine | Git-Server für Run Scripts Repo. |
| `Pipe:Scripts:RepositoriesPath` | `data/script-repos` | Wohin Repositories für einen Lauf geklont werden. |
| `Pipe:Scripts:Interpreters` | keine | Interpreter je Dateiendung, zum Beispiel `.py` → `C:\Python313\python.exe`. |
| `Pipe:Scripts:GitPath` | `git` | Die git-Kommandozeile. |

Relative Pfade gelten relativ zum Arbeitsordner der App.

Beispiel (`_config/pipe.config`):

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

Die Startskripte lauschen standardmäßig auf `http://localhost:5100`. Um das zu ändern, etwa für eine zweite Instanz auf demselben Rechner, setze `urls` oben in `pipe.config` (neben `Pipe`, nicht darin):

```json
{
  "urls": "http://localhost:5101",
  "Pipe": { ... }
}
```

`urls` in `pipe.config` gewinnt gegen `ASPNETCORE_URLS`; `--urls` auf der Kommandozeile gewinnt gegen beide. Der Windows-Dienst übergibt seinen Port mit `--urls` (`gview-pipe-service.bat install <name> <port>`). Das Docker-Image nutzt `http://*:8080`.

## Grenzen, die nicht in der Konfiguration stehen

Einige Optionen lassen sich nur im Code setzen (`Program.cs` des Shell), zum Beispiel wie viele Executions je Workflow behalten werden (50), wie lange ein Wait-Node höchstens wartet (eine Stunde), die JavaScript-Grenzen und die maximale Body-Größe eines Webhooks (10 MB). Frag deinen Entwickler, wenn du sie ändern willst.

Pro Workflow stellst du die **maximale Zahl gleichzeitiger Läufe** (Vorgabe 1) im Workflow-Panel des Editors ein. Weitere gleichzeitige Läufe werden übersprungen, und ein Webhook-Aufrufer bekommt `429`.
