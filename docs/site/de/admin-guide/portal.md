# Anwender-Portal

Das Portal ist eine kleine Web-App für Menschen, die Workflows **ausführen**, aber nicht bearbeiten. Sie sehen die für sie freigegebenen Workflows in Kategorien, füllen ein Formular aus, warten auf das Ergebnis oder lassen den Lauf im Hintergrund weiterlaufen und stoppen ihre eigenen Läufe.

Das Portal ist eine eigene App (`gView.Pipe.Portal`). Es hat keine Engine und keine Tools; es ruft mit einem API-Key die [Portal-Endpunkte](api.md#portal-endpunkte) des Backends (der normalen gView.Pipe-App) auf. Es kann auf einem anderen Server laufen, zum Beispiel im Internet, während das Backend im internen Netz bleibt:

```
Browser  →  Portal-Server  →  Backend (REST API)
```

Der Key bleibt auf dem Portal-Server. Der Browser spricht nie mit dem Backend und sieht den Key nie.

## Backend einrichten

1. Lege auf der Seite **API keys** einen Key nur mit dem Recht **Portal** an. Kopiere ihn; er wird nur einmal angezeigt.
2. Lass nur den Portal-Server das Backend erreichen (VPN, Firewall-Regel oder privates Netz). Ein gestohlener Key könnte sonst als beliebiger Anwender handeln.
3. Gib Workflows frei: Schalte in den [Workflow-Einstellungen](../user-guide/editor.md#einstellungen-des-workflows) *Publish in portal* ein. Die Gruppe des Workflows ist seine Kategorie im Portal. Füge einen **Form Trigger** für die Eingaben und einen **Result**-Node für das hinzu, was der Anwender am Ende sehen soll.

## Portal konfigurieren

Die Einstellungen kommen aus `appsettings.json`, aus `portal.config.json` neben der App und aus der Umgebung (`Portal__BackendUrl`).

| Einstellung | Bedeutung |
|---|---|
| `Portal:BackendUrl` | Adresse des Backends, z. B. `https://pipe.internal:7240`. Nötig. |
| `Portal:ApiKey` | Der Key mit dem Recht *Portal*. Nötig. Bevorzuge die Umgebungsvariable `Portal__ApiKey`. |
| `Portal:Title` | Name in der Titelleiste (Standard `gView.Pipe`). |
| `Portal:PollSeconds` | Sekunden zwischen zwei Statusabfragen, solange ein Lauf läuft (Standard 2). |
| `Portal:Auth` | Der Login der Anwender, dieselben Einstellungen wie [`Pipe:Auth`](authentication.md): `Mode` ist `None`, `Forms` oder `Oidc`. |

!!! warning
    Mit `Mode` *None* ist jeder, der das Portal erreicht, der Anwender *anonymous* und sieht die Läufe aller anonymen Anwender. Verwende einen Login, sobald mehr als du das Portal erreichen können.

Der Name des angemeldeten Anwenders geht mit jeder Anfrage ans Backend. Läufe zeigen in der Historie des Editors `portal:<Name>` als Starter, und Anwender sehen und stoppen nur ihre eigenen Läufe.

Gestartet wird es wie die Shell: `dotnet run --project src/gView.Pipe.Portal` (Port 5250 in der Entwicklung).

## Was Anwender tun können

- **Workflows**: alle veröffentlichten Workflows nach Kategorie, mit Suche.
- **Formular**: die Felder des Form Triggers. Felder mit Muster werden schon beim Tippen geprüft; das Backend prüft jeden Start nochmal.
- **Warten oder Hintergrund**: Der Autor legt den Standard fest und ob Anwender umschalten dürfen. Ein Lauf, auf den der Anwender wartet, läuft nach Ablauf des Warte-Timeouts im Hintergrund weiter.
- **My runs**: die Läufe des Anwenders mit Status. Laufende Läufe haben einen *Stop*-Button, außer der Workflow verbietet es. Beendete Läufe haben *Run again*, das das Formular mit den Werten dieses Laufs öffnet (Passwörter werden nie aufbewahrt).
- **Ergebnis**: Text, Markdown (ohne rohes HTML) oder eine Tabelle, wie es der Result-Node festlegt. Anwender sehen nie rohe Items oder die Details von Fehlern.

## Mail, wenn ein Lauf fertig ist

Ein Anwender kann beim Start *Mail me when it is done* ankreuzen. Die Mail sendet ein Workflow, den du selbst baust; so bestimmst du Text und SMTP-Server:

1. Lege einen Workflow mit einem **Execute Workflow Trigger** und einem **Send Email**-Node an, z. B. *portal-mail*. Der Trigger liefert ihm ein Item:

    ```json
    {
      "to": "ada@example.com",
      "user": "ada",
      "workflow": { "id": "invoice", "name": "Invoice" },
      "execution": { "id": "…", "status": "success", "startedAt": "…", "finishedAt": "…", "url": "https://portal.example.com/jobs/invoice/…" }
    }
    ```

    Verwende im Send-Email-Node `{{ $json.to }}` als Empfänger, und als Text z. B. `Dein Lauf von {{ $json.workflow.name }} ist beendet: {{ $json.execution.status }}. {{ $json.execution.url }}`. Das Item enthält nie das Ergebnis des Laufs.
2. Setze auf dem Backend `Pipe:Portal:NotifyWorkflowId` auf seine Id und `Pipe:Portal:PublicUrl` auf die Adresse des Portals, damit `url` gefüllt wird.

Die Option erscheint nur, wenn das Backend einen Benachrichtigungs-Workflow hat und der Anwender eine Adresse hat: aus dem Login (OIDC-Claim `email`) oder im Modus forms das `Email` des Benutzers in `Portal:Auth:Users` (den Einstellungen des Portals, nicht des Backends). Es wird keine Mail gesendet, wenn der Anwender das Ergebnis schon beim Warten bekommen hat. Ein Lauf, der beim Neustart des Backends noch läuft, sendet seine Mail nicht.

## Ergebnisse und Historie

Anwender sehen ihre eigenen Läufe mit den eingegebenen Werten und dem Ergebnis für `Pipe:Portal:HistoryDays` Tage (Standard 30); danach nur noch den Status. Werte von Passwortfeldern werden nicht gezeigt. Die Historie des Editors behält die Daten des Laufs wie gewohnt; schalte bei Workflows mit Geheimnissen *Save item data of executions* aus.

## Grenzen

Ein Anwender darf auf dem Backend `Pipe:Portal:MaxRunsPerUser` Läufe gleichzeitig haben (Standard 3), und ein Workflow kann nur einen Lauf gleichzeitig erlauben. Darüber hinaus zeigt das Portal die Meldung des Backends.
