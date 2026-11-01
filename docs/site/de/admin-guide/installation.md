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
    Das Startskript bindet nur an `localhost`. Damit andere Rechner die App erreichen, setze einen Reverse Proxy davor (siehe [Betrieb](operations.md#hinter-einem-reverse-proxy)) oder ändere `--urls` im Skript. Es gibt noch **keine Anmeldung**: Setze die App nicht direkt einem Netz aus, dem du nicht vertraust (siehe [Sicherheit](security.md)).

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

gView.Pipe ist eine normale ASP.NET-Core-App (`dotnet gView.Pipe.Shell.dll`). Damit sie dauerhaft läuft, nimm, was deine Plattform bietet: einen Windows-Dienst (zum Beispiel mit NSSM oder `sc.exe`), eine systemd-Unit oder eine Docker-Restart-Policy (`--restart unless-stopped`). Starte sie unter einem Konto mit **eingeschränkten Rechten**, denn Skripte laufen mit den Rechten dieses Kontos (siehe [Sicherheit](security.md)).

Aktive Workflows mit einem Cron- oder Webhook-Trigger werden beim Start der App wieder scharf geschaltet.

## Dokumentation

Die App liefert diese Dokumentation selbst unter `/docs` aus (zum Beispiel `http://localhost:5100/docs`), in der Sprache des Browsers (Englisch oder Deutsch). Der Link **Docs** in der oberen Leiste öffnet sie. Sie funktioniert offline und passt immer zur installierten Version.

## Installation prüfen

- Die Startseite öffnet sich und zeigt die Beispiel-Workflows.
- Das Log beim Start nennt `Plugins: <n> node types from <m> assemblies`. Konnte ein Plugin nicht geladen werden, folgt eine Warnung (siehe [Fehlersuche](troubleshooting.md)).
