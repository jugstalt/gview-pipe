# Kommandozeile

`pipe` ist ein Kommandozeilen-Client der [REST API](api.md) für Skripte und CI-Pipelines: Workflows aus Dateien bereitstellen, Läufe starten und auf sie warten, Ausführungen ansehen.

## Installieren

```bash
dotnet tool install --global gView.Pipe.Cli
pipe --help
```

Es braucht die .NET-10-Runtime und einen [API-Schlüssel](api.md#api-schlussel) mit den Rechten für das, was du tust (read, run, deploy).

## Verbinden

Setze die Adresse des Servers und den Schlüssel als Umgebungsvariablen, damit der Schlüssel weder in der Prozessliste noch in den Logs der Pipeline erscheint:

=== "bash"

    ```bash
    export PIPE_URL=https://pipe.example.com
    export PIPE_API_KEY=pipe_...
    ```

=== "PowerShell"

    ```powershell
    $env:PIPE_URL = "https://pipe.example.com"
    $env:PIPE_API_KEY = "pipe_..."
    ```

`--url` und `--key` tun dasselbe für einen Aufruf.

## Befehle

| Befehl | Tut |
|---|---|
| `pipe workflows list [--json]` | Listet die Workflows. |
| `pipe workflows get <id>` | Gibt den Workflow als JSON aus. |
| `pipe workflows export <id> --out <Datei oder Ordner>` | Speichert den Workflow als Datei; bei einem Ordner heißt sie `<id>.json`. |
| `pipe workflows deploy <Datei oder Ordner>... [--dry-run] [--force]` | Legt Workflows aus JSON-Dateien an oder ersetzt sie (Ordner: alle `*.json` darunter). |
| `pipe workflows delete <id>` | Löscht einen Workflow. |
| `pipe workflows activate <id>`, `deactivate <id>` | Schaltet seine Trigger ein oder aus. |
| `pipe run <id> [--wait] [--input <Datei oder ->] [--environment <Name>] [--timeout <Sekunden>] [--json]` | Startet einen Lauf. |
| `pipe executions list [--workflow <id>] [--status <Status>] [--limit <n>] [--json]` | Listet Ausführungen, neueste zuerst. |
| `pipe executions get <workflowId> <id>` | Eine Ausführung mit ihren Node-Läufen. |
| `pipe executions cancel <workflowId> <id>` | Stoppt einen laufenden oder wartenden Lauf. |
| `pipe executions replay <workflowId> <id> [--from <nodeId>]` | Führt eine Ausführung erneut aus. |

## Bereitstellen

```bash
pipe workflows deploy workflows/            # jede *.json unter dem Ordner
pipe workflows deploy hello-pipe.json --dry-run
```

Die Datei ist das JSON eines Workflows, wie es `pipe workflows export` schreibt oder wie es in `data/workflows` liegt; die `id` darin benennt den Workflow. Der Server prüft ihn vor dem Speichern; Probleme werden aufgelistet, und der Exit-Code ist 2.

- Ein vorhandener Workflow wird nur ersetzt, wenn ihn seit dem Lesen durch den Befehl niemand geändert hat, zum Beispiel im Editor. Sonst bricht der Befehl mit einer Meldung ab; `--force` ersetzt ihn trotzdem.
- `--dry-run` meldet nur *would create* oder *would update*. Er prüft den Workflow nicht; das macht der Server beim echten Deploy.
- `deploy` löscht nie. Entferne Workflows einzeln mit `pipe workflows delete`.
- Credentials, die es auf dem Server nicht gibt, werden als Warnungen gemeldet.

## Ausführen

```bash
pipe run hello-pipe                                   # startet ihn, gibt die Execution-Id aus
pipe run hello-pipe --wait --input item.json          # wartet und gibt die Items des letzten Nodes aus
echo '{ "name": "Ada" }' | pipe run hello-pipe --wait --input -
```

`--input` ist ein JSON-Objekt (ein Item) oder ein Array von Objekten; `-` liest es von der Standardeingabe. Der Lauf startet wie ein getriggerter Lauf, siehe [Einen Workflow ausführen](api.md#einen-workflow-ausfuhren).

## Exit-Codes

| Code | Bedeutung |
|---|---|
| 0 | Erledigt. Ohne `--wait` gilt ein gestarteter Lauf als erledigt. |
| 1 | `pipe run --wait`: Der Lauf endete mit dem Status `error`. |
| 2 | Falscher Aufruf, oder der Server hat ihn abgelehnt (`401` Schlüssel, `404`, `422` ungültiger Workflow, `429` Warteschlange voll). |

Ein Pipeline-Schritt kann den Befehl deshalb direkt verwenden: Ein fehlgeschlagener Lauf oder ein abgelehntes Deploy lässt den Schritt fehlschlagen.

```yaml
# Beispiel: GitHub Actions
- run: dotnet tool install --global gView.Pipe.Cli
- run: pipe workflows deploy workflows/
  env:
    PIPE_URL: ${{ vars.PIPE_URL }}
    PIPE_API_KEY: ${{ secrets.PIPE_API_KEY }}
```
