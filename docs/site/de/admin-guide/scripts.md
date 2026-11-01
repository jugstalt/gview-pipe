# Skripte ausführen

Die Skript-Nodes führen vorhandene Skripte (`.bat`, `.cmd`, `.ps1`, `.py`, `.sh`) aus Workflows heraus aus. Code auf dem Server auszuführen ist von Natur aus riskant, deshalb entscheidest **du**, was laufen darf: Workflows können keinen Pfad und keinen Befehl eintippen. Sie können nur Dateien aus Quellen wählen, die du freigibst.

## Voraussetzungen

Installiere das Plugin `gView.Pipe.Plugins.Scripts` (siehe [Plugins](plugins.md)). Ohne es gibt es die Nodes nicht.

Der Interpreter richtet sich nach der Dateiendung. Verfügbar ist nur, was das Betriebssystem ausführen kann:

| Endung | Interpreter |
|---|---|
| `.bat`, `.cmd` | `cmd.exe` (Windows) |
| `.ps1` | `pwsh`, sonst Windows PowerShell |
| `.py` | `python` |
| `.sh` | `bash` |

Einen Interpreter änderst du mit `Pipe:Scripts:Interpreters`, zum Beispiel `".py": "C:\\Python313\\python.exe"`.

## Skriptordner

Ein Skriptordner ist ein Ordner auf dem Server, den du unter einem Namen freigibst:

```json
"Pipe": {
  "Scripts": {
    "ScriptRoots": { "scripts": "D:\\ops\\scripts", "maintenance": "D:\\ops\\maintenance" }
  }
}
```

Die Nodes **Run Script** und **Run Scripts Folder** listen die Dateien dieser Ordner auf, Unterordner eingeschlossen.

!!! warning
    Halte Skriptordner getrennt von den Ordnern, die du für Write File freigibst (`Pipe:FileRoots`). Sonst könnte ein Workflow ein Skript schreiben und es danach ausführen.

## Git-Server

**Run Scripts Repo** holt Skripte aus einem Repository eines Git-Servers, den du konfigurierst. Gitea und GitHub werden unterstützt:

```json
"Pipe": {
  "Scripts": {
    "GitServers": {
      "company": {
        "Provider": "gitea",
        "Url": "https://git.example.com",
        "Owner": "ops",
        "CredentialId": "git-token",
        "RepositoryFilter": "^ops-"
      }
    }
  }
}
```

| Einstellung | Bedeutung |
|---|---|
| `Provider` | `gitea` oder `github`. Bestimmt, welche API Repositories, Branches und Dateien auflistet. |
| `Url` | Basis-URL, zum Beispiel `https://github.com`. |
| `Owner` | Die Organisation oder der Benutzer, dessen Repositories angeboten werden. |
| `CredentialId` | Die Id eines Pipe-Credentials (Basic Auth mit Benutzer und Passwort oder Token, oder Bearer Token). Lege es vorher in der Credential-Liste an. |
| `RepositoryFilter` | Optionaler regulärer Ausdruck; nur Repositories, deren Name passt, werden angeboten. |

Jeder Lauf macht einen flachen Clone (`git clone --depth 1`) in einen temporären Ordner unterhalb von `Pipe:Scripts:RepositoriesPath`, führt die Skripte dort aus und löscht den Ordner danach, auch nach einem Fehler oder Abbruch. Reste werden beim Start der App entfernt. Es gibt nie einen veralteten Stand. Das Credential erreicht git als HTTP-Header in der Umgebung, nie in der URL oder in einem Log. Es funktioniert nur HTTPS (oder lokale Pfade), kein SSH.

Die **git-Kommandozeile muss auf dem Server installiert sein** (im Docker-Image ist sie enthalten).

## Wie ein Skript läuft

- Ein Prozess je Skript, mit dem Ordner des Skripts (bei Run Scripts Repo das Repository-Wurzelverzeichnis) als Arbeitsordner.
- Die Umgebung enthält die Umgebung der App plus `PIPE_ITEM`, `PIPE_WORKFLOW`, `PIPE_EXECUTION`, die Variablen des Nodes und, wenn der Node ein Credential hat, `PIPE_CREDENTIAL_<FELD>`. Credential-Werte werden in der Ausgabe und in Logs maskiert.
- Das Timeout beträgt standardmäßig 10 Minuten je Skript. Ein Timeout oder ein Abbruch beendet den ganzen Prozessbaum.
- Die Ausgabe ist auf 1 MB je Stream begrenzt und wird zusätzlich ins Log des Nodes geschrieben.
- Ein Skript **schlägt fehl**, wenn sein Exit-Code nicht 0 ist oder eine Zeile seiner Ausgabe zum Fehlermuster des Nodes passt (ein regulärer Ausdruck).

Wie ein fehlschlagendes Skript den Workflow beeinflusst, steht unter [Fehlerbehandlung](../user-guide/error-handling.md).

## Sicherheit

Skripte laufen mit den **Rechten des Kontos der App**. Starte die App unter einem Konto, das nur tun darf, was die Skripte brauchen. Siehe [Sicherheit](security.md).
