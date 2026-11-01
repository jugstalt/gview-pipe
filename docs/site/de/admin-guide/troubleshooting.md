# Fehlersuche

## Die App startet nicht

- **Port belegt:** Ein anderes Programm nutzt den Port. Ändere ihn mit `--urls`.
- **Runtime fehlt:** `dotnet gView.Pipe.Shell.dll` braucht die .NET-10-ASP.NET-Core-Runtime (bei Docker nicht nötig).
- **Kein Schreibzugriff:** Das Konto braucht Schreibzugriff auf den Ordner `data`.

## Ein Node fehlt in der Palette

Das Plugin ist nicht installiert oder konnte nicht geladen werden. Prüfe:

1. Der Ordner heißt `plugins/<Name>/` und enthält `<Name>.dll` mit genau dem Namen des Ordners.
2. Das Log beim Start. Ein Plugin, das sich nicht laden lässt, erzeugt eine Warnung, die mit `Plugins:` beginnt.
3. Du hast die App nach dem Kopieren des Plugins neu gestartet.

## Skript-Nodes führen kein Skript aus

- Das Skript liegt nicht in einem freigegebenen Ordner, oder der Ordner steht nicht in `Pipe:Scripts:ScriptRoots`.
- Der Interpreter ist nicht installiert (`python`, `pwsh`, `bash`). Setze den Pfad in `Pipe:Scripts:Interpreters`.
- Run Scripts Repo: `git` ist nicht installiert, der Git-Server oder sein Credential ist falsch, oder der Repository-Filter blendet das Repository aus. Der Node zeigt Hinweise auf fehlende Einstellungen.
- In Docker: `.ps1`-, `.bat`- und `.cmd`-Skripte laufen im Linux-Image nicht.
- Der Node meldet **fehlgeschlagen**, obwohl das Skript in Ordnung aussieht: Ein Exit-Code ungleich 0 oder eine Zeile, die zum Fehlermuster passt, lässt ihn fehlschlagen. Sieh im Log des Nodes die Ausgabe des Skripts an.

## Ein Webhook antwortet mit einem Fehler

| Antwort | Ursache |
|---|---|
| `404` | Der Workflow ist nicht aktiv, oder der Pfad stimmt nicht. Webhook-Pfade sind klein geschrieben. |
| `405` | Die Methode weicht von der im Trigger eingestellten ab. |
| `401` | Das Credential fehlt oder ist falsch. |
| `413` | Der Body ist größer als 10 MB. |
| `429` | Der Workflow läuft schon, und sein Limit gleichzeitiger Läufe ist erreicht. |
| `500` | Der Lauf ist fehlgeschlagen. Der Body enthält den Fehler und die `executionId`; suche den Lauf in der Historie. Siehe [Fehlerbehandlung](../user-guide/error-handling.md). |

Lässt sich ein Workflow nicht aktivieren, weil sein Pfad schon belegt ist, zeigt der Editor, welcher Workflow und welcher Node ihn nutzen.

## Credentials funktionieren nach einem Umzug oder einer Wiederherstellung nicht mehr

Der Ordner `data/keys/` passt nicht zu `data/credentials/`. Stelle beide aus demselben Backup wieder her, oder gib die Credentials neu ein.

## Ein Workflow bricht nach einem Neustart ab

Laufende Läufe enden mit einem Neustart. Aktive Workflows werden wieder scharf geschaltet, aber ein wartender Wait-Node hat seinen Lauf verloren.

## Wo du nachsiehst

- Das Anwendungs-Log (Konsole, Dienst-Log oder `docker logs`).
- Die Execution-Historie des Workflows: Jeder Node zeigt Eingabe, Ausgabe und Log.
