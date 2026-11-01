# Betrieb

## Hinter einem Reverse Proxy

Für den Zugriff von anderen Rechnern und für HTTPS setzt du einen Reverse Proxy (IIS, nginx, Traefik, …) vor die App. Die App selbst lauscht auf einfachem HTTP.

- Leite `/` an die App weiter. Der Editor nutzt eine Live-Verbindung (Blazor Server, WebSockets): Schalte WebSocket-Weiterleitung ein und trenne ruhende Verbindungen nicht zu früh.
- Webhooks liegen unter `/webhook/<Pfad>`. Willst du nur Webhooks ins Internet geben, leite nur `/webhook/` weiter und halte den Editor intern.
- **Der Editor hat keine Anmeldung.** Wer ihn öffnen kann, kann Workflows anlegen und alles ausführen, was du freigegeben hast. Schütze ihn mit dem Proxy (Authentifizierung, IP-Beschränkung) oder einem VPN.

## Webhooks im Betrieb

- Webhooks funktionieren nur, solange der Workflow **Active** ist. Nach einem Neustart werden alle aktiven Workflows wieder scharf geschaltet.
- Ein Pfad gehört in der ganzen App genau einem Workflow. Ein zweiter Workflow mit demselben Pfad lässt sich nicht aktivieren, und der Editor zeigt nach dem Speichern den Grund.
- **Setze bei von außen erreichbaren Webhooks immer ein Credential** (Basic Auth, Bearer Token oder API Key Header). Ohne eines kann jeder, der die URL kennt, den Workflow starten.
- Die maximale Body-Größe beträgt 10 MB (Antwort `413`). Aufrufer, die auf den Workflow warten, haben je Webhook ein Timeout (Vorgabe 30 Sekunden). Danach bekommen sie `202`, und der Lauf geht weiter.
- Ein Workflow läuft standardmäßig immer nur einmal gleichzeitig. Weitere Aufrufe bekommen `429`, solange er läuft. Erhöhe **Max concurrent runs** im Workflow-Panel, wenn Aufrufe parallel laufen sollen.
- Läufe werden nicht abgebrochen, wenn der Aufrufer die Verbindung trennt.

Wie Aufrufer Fehler sehen, steht unter [Trigger und Webhooks](../user-guide/triggers.md) und [Fehlerbehandlung](../user-guide/error-handling.md).

## Execution-Historie

Die letzten 50 Executions je Workflow bleiben in `data/executions/` erhalten. Schaltest du im Workflow-Panel **Save item data of executions** aus, werden nur Status, Zähler und Logs gespeichert. Das ist nützlich, wenn Items sensible Daten enthalten.

Laufende Executions liegen im Speicher des Prozesses. Ein Neustart beendet sie, und ein wartender Wait-Node verliert seinen Lauf.

## Backup

Sichere den ganzen Ordner `data`, dazu `files` und `scripts`, wenn du sie nutzt:

| Ordner | Inhalt |
|---|---|
| `data/workflows/` | Eine JSON-Datei je Workflow. Geeignet für Git. |
| `data/groups.json` | Gruppen der Startseite. |
| `data/credentials/` | Credentials, verschlüsselt. |
| `data/keys/` | Die Schlüssel, die die Credentials entschlüsseln. |
| `data/executions/` | Historie. Optional. |

!!! important
    Stelle `credentials/` und `keys/` **gemeinsam** wieder her. Ohne die Schlüssel lassen sich gespeicherte Credentials nicht lesen, und du musst sie neu eingeben.

Für eine konsistente Kopie hältst du die App an oder kopierst, solange nichts läuft. Dateien werden über eine temporäre Datei geschrieben und dann an ihren Platz verschoben.

## Updates

**ZIP:** Entpacke die neue Version neben der alten, halte die alte App an und starte das neue Startskript. `data`, `files` und `scripts` bleiben, wo sie sind. Behalte den alten Versionsordner, bis die neue Version läuft; zum Zurückgehen startest du sein Skript wieder.

**Docker:** Ziehe das neue Image und erzeuge den Container mit denselben Volumes neu.

Nodes speichern die Version ihrer Parameter und werden automatisch migriert, wenn ein älterer Workflow läuft. Sichere vor einem Update trotzdem `data`.

## Logs

Die App schreibt ins Konsolen-Log (und ins Aspire-Dashboard, wenn du sie mit Aspire startest). Achte im Log auf Plugin-Warnungen beim Start und auf Node-Fehler. Die Log-Stufe stellst du im Abschnitt `Logging` von `appsettings.json` ein.
