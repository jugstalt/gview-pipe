# Konfiguration

Die Einstellungen stehen in `_config/pipe.config` im Arbeitsordner der App (im Abschnitt `Pipe`, JSON mit Kommentaren). Fehlt die Datei beim Start, legt die App sie aus der Vorlage `_setup/proto/pipe.config` an; danach passt du sie an. `appsettings.json` enthält nur ASP.NET-Core-Einstellungen wie `Logging`. Umgebungsvariablen und die Kommandozeile überschreiben `pipe.config`. Die Startskripte im ZIP setzen die Ordner als Umgebungsvariablen.

Die Anmeldung hat eine eigene Datei `_config/auth.config`, die genauso aus `_setup/proto/auth.config` entsteht (siehe [Anmeldung](authentication.md)).

Im Docker-Image ist `/app/_config` ein Volume, damit die Dateien Updates überstehen.

## Konfiguration über Updates behalten

Die Variable `PIPE_CONFIG_PATH` verlegt den Ordner `_config`. Die Startskripte und `gview-pipe-service.bat install` setzen sie auf `<root>\_config`, neben die Versionsordner. So hat jede Installation (jeder Root-Ordner) ihre eigene `pipe.config` und behält sie über Updates. Beim ersten Start legt die App sie dort aus der Vorlage an. Bearbeite `<root>\_config\pipe.config`; der Versionsordner hat kein eigenes `_config`.

## Einstellungen

| Schlüssel | Vorgabe | Bedeutung |
|---|---|---|
| `Pipe:DataPath` | `data` | Ordner für `workflows/`, `groups.json`, `executions/`, `credentials/` und die Data-Protection-`keys/`. |
| `Pipe:DocsPath` | `docs` | Ordner mit der gebauten Dokumentation, die unter `/docs` ausgeliefert wird. ZIP und Docker-Image enthalten sie. Ohne ihn führen die Docs-Links zur öffentlichen Dokumentation. |
| `Pipe:DocsUrl` | | Adresse, auf die die Docs-Links und die ?-Buttons der Oberfläche zeigen. Standard: `/docs`, wenn die gebaute Dokumentation da ist, sonst https://docs.gviewonline.com/other/gview-pipe/. |
| `Pipe:PluginPath` | `plugins` | Ordner mit externen Plugins, je Plugin ein Unterordner: `plugins/<Name>/<Name>.dll`. |
| `Pipe:FileRoots` | `files` → `files` | Ordner, die Read File und Write File benutzen dürfen, nach Namen. Außerhalb davon ist nichts erreichbar. |
| `Pipe:Scripts:ScriptRoots` | `scripts` → `scripts` | Skriptordner für die Skript-Nodes, nach Namen (siehe [Skripte ausführen](scripts.md)). |
| `Pipe:Scripts:GitServers` | keine | Git-Server für Run Scripts Repo. |
| `Pipe:Scripts:RepositoriesPath` | `data/script-repos` | Wohin Repositories für einen Lauf geklont werden. |
| `Pipe:Scripts:Interpreters` | keine | Interpreter je Dateiendung, zum Beispiel `.py` → `C:\Python313\python.exe`. |
| `Pipe:Scripts:GitPath` | `git` | Die git-Kommandozeile. |
| `Pipe:Environment` | `DEFAULT` | Das Environment, in dem diese Instanz läuft. |
| `Pipe:Environments` | `TEST`, `STAGE`, `PROD` | Environments, die Gruppen und Workflows neben `DEFAULT` wählen können. |
| `Pipe:Variables` | keine | Instanz-Variablen je Environment. |
| `Pipe:SecretVariables` | keine | Namen von Instanz-Variablen, die in Logs und Skriptausgaben maskiert werden. |
| `Pipe:ErrorWorkflowId` | keine | Id des [Fehler-Workflows](../user-guide/notifications.md) für alle Workflows, deren Gruppe und Workflow keinen festlegen, zum Beispiel `error-mail`. |
| `Pipe:PublicUrl` | keine | Adresse, unter der Benutzer die App öffnen, zum Beispiel `https://pipe.example.com`. Fehler-Workflows bekommen einen Link zum fehlgeschlagenen Lauf (`execution.url`). |
| `Pipe:ExecutionRetention` | `50` | Executions, die je Workflow behalten werden. Ein Workflow kann einen eigenen Wert setzen. |
| `Pipe:ExecutionRetentionDays` | `0` | Tage, die Executions behalten werden; `0` bedeutet keine Grenze. Ein Workflow kann einen eigenen Wert setzen. Ältere Executions werden einmal am Tag und nach jedem Lauf gelöscht. |
| `Pipe:WorkflowVersions` | `50` | Gespeicherte [Versionen](operations.md#workflow-versionen) pro Workflow; `0` behält keine. |
| `Pipe:AuditRetentionDays` | `365` | Tage, die das [Audit-Log](operations.md#audit-log) behalten wird, in ganzen Monaten; `0` bedeutet keine Grenze. |

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

## Environments und Variablen

Jede Instanz läuft in einem **Environment**: ein Testserver in `TEST`, der Produktionsserver in `PROD`. Gruppen und Workflows erben es, wenn sie kein eigenes setzen, siehe [Variablen und Environments](../user-guide/variables.md). Anwender können das Environment der Instanz in der App nicht ändern.

```json
{
  "Pipe": {
    "Environment": "PROD",
    "Environments": [ "TEST", "STAGE", "PROD" ],
    "Variables": {
      "DATABASE_SERVER": { "DEFAULT": "db-test", "PROD": "db-prod" },
      "DATABASE_PASSWORD": { "PROD": "..." }
    },
    "SecretVariables": [ "DATABASE_PASSWORD" ]
  }
}
```

Variablen der Konfiguration sind die unterste Ebene. Werte, die auf der Seite **Variables** der App eingetragen werden, gewinnen je Environment gegen sie. Die App speichert sie in `variables.json` im Datenordner; geheime Werte werden mit den Schlüsseln in `keys/` verschlüsselt. Geheime Werte von Gruppen und Workflows werden genauso verschlüsselt, eine Workflow-Datei, die in eine andere Installation kopiert wird, verliert sie also.

## Umgebungsvariablen

Für die Verschachtelung nimmst du zwei Unterstriche:

```
Pipe__DataPath=/var/lib/gview-pipe/data
Pipe__FileRoots__import=/srv/import
Pipe__Scripts__ScriptRoots__scripts=/srv/scripts
Pipe__Scripts__GitServers__company__Url=https://git.example.com
Pipe__Environment=PROD
Pipe__Variables__DATABASE_SERVER__PROD=db-prod
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

Einige Optionen lassen sich nur im Code setzen (`Program.cs` des Shell), zum Beispiel wie lange ein Wait-Node höchstens wartet (eine Stunde), die JavaScript-Grenzen und die maximale Body-Größe eines Webhooks (10 MB). Frag deinen Entwickler, wenn du sie ändern willst.

Pro Workflow stellst du die **maximale Zahl gleichzeitiger Läufe** (Vorgabe 1) im Workflow-Panel des Editors ein. Weitere gleichzeitige Läufe werden übersprungen, und ein Webhook-Aufrufer bekommt `429`, sofern **Max queued runs** getriggerte Läufe nicht warten lässt.
