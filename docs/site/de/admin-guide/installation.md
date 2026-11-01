# Installation

gView.Pipe braucht die .NET-10-Runtime (ASP.NET Core). Das Docker-Image bringt sie mit.

## ZIP (Windows und Linux)

Lade `gview-pipe-win64-<Version>.zip` oder `gview-pipe-linux64-<Version>.zip` herunter und entpacke sie in einen Ordner deiner Wahl, zum Beispiel `C:\gview-pipe`. Das ZIP enthält je Version einen Ordner:

```
gview-pipe\
  0.1.0\
    pipe\                 die App und ihre Plugins
    gview-pipe.bat        Startskript (unter Linux gview-pipe.sh)
  data\                   wird beim ersten Start angelegt: Workflows, Executions, Credentials, Schlüssel
  files\                  Ordner für Read File / Write File
  scripts\                Ordner für Run Script
```

`data`, `files` und `scripts` liegen **neben** den Versionsordnern und überstehen deshalb ein Update. Das Startskript setzt ihren Ort für dich.

Starte die App mit dem Startskript. Sie lauscht auf `http://localhost:5100`. Öffne diese Adresse im Browser.

!!! note
    Das Startskript bindet nur an `localhost`. Damit andere Rechner die App erreichen, setze einen Reverse Proxy davor (siehe [Betrieb](operations.md#hinter-einem-reverse-proxy)) oder setze `urls` in `<root>\_config\pipe.config` (siehe [Konfiguration](configuration.md#lauschadresse)). Es gibt noch **keine Anmeldung**: Setze die App nicht direkt einem Netz aus, dem du nicht vertraust (siehe [Sicherheit](security.md)).

Beim ersten Start mit leerem Ordner `data` kopiert gView.Pipe die Beispiel-Workflows in die Gruppe **Samples**.

## Docker

```bash
docker run -d --name gview-pipe -p 8080:8080 \
  -v gview-pipe-data:/app/data \
  ghcr.io/jugstalt/gview-pipe:latest
```

- Die App lauscht im Container auf Port 8080.
- `/app/data` enthält den gesamten Laufzeitzustand: Binde ein Volume oder einen Ordner ein, sonst gehen die Daten mit dem Container verloren.
- Das Image enthält `git` (für Run Scripts Repo) und `python3` (für `.py`-Skripte). Es hat weder PowerShell noch eine Windows-Shell, also laufen `.ps1`-, `.bat`- und `.cmd`-Skripte darin nicht.
- Für Read File / Write File und Skriptordner bindest du die Ordner ein und nennst sie in der Konfiguration, zum Beispiel `-v /srv/pipe/files:/app/files -v /srv/pipe/scripts:/app/scripts`. Die Vorgaben sind `files` und `scripts` unterhalb von `/app`.

Einstellungen übergibst du als Umgebungsvariablen, siehe [Konfiguration](configuration.md#umgebungsvariablen).

## Automatisch starten

Das Startskript lässt die App nur laufen, solange du angemeldet bist. Damit sie auf einem Server dauerhaft läuft, betreibe sie als Windows-Dienst, im IIS, als systemd-Unit oder in Docker. Starte sie in jedem Fall unter einem Konto mit **eingeschränkten Rechten**, denn Skripte laufen mit den Rechten dieses Kontos (siehe [Sicherheit](security.md)).

Aktive Workflows mit einem Cron- oder Webhook-Trigger werden beim Start der App wieder scharf geschaltet.

### Windows-Dienst

Im Versionsordner liegt `gview-pipe-service.bat`. Führe es in einer Eingabeaufforderung **als Administrator** aus:

```
gview-pipe-service.bat install [name] [port]
gview-pipe-service.bat uninstall [name]
```

`install` legt den Dienst an und startet ihn:

- Dienstname `gview-pipe`, mit Namen `gview-pipe-<name>`. Er startet automatisch (verzögert) und wird nach einem Absturz neu gestartet.
- Er lauscht auf `http://localhost:<port>` (Vorgabe `5100`), wie das Startskript.
- Er läuft unter dem virtuellen Konto `NT SERVICE\gview-pipe[-<name>]`, das nur in `data`, `files`, `scripts` und `_config` schreiben darf.
- `data`, `files`, `scripts` und `_config` neben den Versionsordnern werden als Umgebungsvariablen des Dienstes gesetzt, wie im Startskript.

**Mehrere Instanzen** auf einem Server, zum Beispiel Produktion und Test: Entpacke das ZIP je Instanz in einen eigenen Root-Ordner und gib jeder einen Namen und einen Port.

```
D:\gview-pipe-prod\0.1.0\gview-pipe-service.bat install prod 5100
D:\gview-pipe-test\0.1.0\gview-pipe-service.bat install test 5200
```

`install` verweigert einen Namen, dessen Dienst zu einem anderen Root-Ordner gehört.

**Update:** Entpacke die neue Version in denselben Root-Ordner und führe ihr `gview-pipe-service.bat install` mit demselben Namen und Port aus. Das Skript stoppt den Dienst, stellt ihn auf die neue Version um und startet ihn wieder. Zurück geht es mit `install` der alten Version.

**Eigenes Konto:** Für Netzwerkfreigaben oder Git mit Windows-Anmeldung reicht das virtuelle Konto nicht. Trage in den Diensteigenschaften (Reiter **Anmelden**) ein Domänen- oder lokales Konto ein und gib ihm Schreibrechte auf dieselben Ordner.

Das Log geht ins Windows-Ereignisprotokoll (Anwendungsprotokoll, Quelle `gView.Pipe`, Warnungen und Fehler).

### IIS

gView.Pipe läuft im IIS wie jede ASP.NET-Core-App:

- Installiere das **ASP.NET Core Hosting Bundle** für .NET 10.
- Lege eine Site oder Anwendung an, deren physischer Pfad der `pipe`-Ordner der Version ist.
- Stelle den Anwendungspool auf **Kein verwalteter Code** und gib seiner Identität Schreibrechte auf `data`, `files`, `scripts` und `_config`.

Im IIS läuft kein Startskript. Setze `PIPE_CONFIG_PATH=<root>\_config` als Umgebungsvariable des Anwendungspools (nicht in der `web.config` der Version, die ein Update ersetzt) und trage die Ordner als absolute Pfade in `<root>\_config\pipe.config` ein (siehe [Konfiguration](configuration.md#konfiguration-uber-updates-behalten)).

### Linux und Docker

Nimm eine systemd-Unit, die `gview-pipe.sh` oder `dotnet gView.Pipe.Shell.dll` mit denselben Umgebungsvariablen startet, oder eine Docker-Restart-Policy (`--restart unless-stopped`).

## Dokumentation

Die App liefert diese Dokumentation selbst unter `/docs` aus (zum Beispiel `http://localhost:5100/docs`), in der Sprache des Browsers (Englisch oder Deutsch). Der Link **Docs** in der oberen Leiste öffnet sie. Sie funktioniert offline und passt immer zur installierten Version.

## Installation prüfen

- Die Startseite öffnet sich und zeigt die Beispiel-Workflows.
- Das Log beim Start nennt `Plugins: <n> node types from <m> assemblies`. Konnte ein Plugin nicht geladen werden, folgt eine Warnung (siehe [Fehlersuche](troubleshooting.md)).
