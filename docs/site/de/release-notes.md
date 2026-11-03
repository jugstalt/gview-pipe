# Versionshinweise

Was in jeder Version neu ist und was du beim Update tun musst. Die vollständige Liste der Änderungen steht im [Changelog](https://github.com/jugstalt/gview-pipe/blob/main/CHANGELOG.md) im Repository (englisch).

Ein Update ersetzt den Versionsordner des [ZIP](admin-guide/installation.md) oder das Docker-Image. Die Ordner `data`, `files` und `scripts` sowie der Ordner `_config` bleiben, wie sie sind. Sichere sie vor dem Update (siehe [Betrieb](admin-guide/operations.md)).

## 0.1.0

Die erste Version.

### Highlights

- Ein visueller Editor für Workflows aus Nodes, die JSON-Items weiterreichen; der [Editor](user-guide/editor.md) zeigt Eingabe, Ausgabe und Log jedes Nodes.
- Trigger: manuell, Cron, Webhook und Formular; Webhooks können wie eine kleine API antworten. Siehe [Trigger und Webhooks](user-guide/triggers.md).
- JavaScript-[Ausdrücke](user-guide/expressions.md) in jedem Parameter und ein Code-Node, ausgeführt in einer Sandbox.
- Variablen und Environments, Gruppen, Sub-Workflows, Error-Workflows und [Benachrichtigung bei Fehlern](user-guide/notifications.md).
- Plugins für Datenbanken (SQL Server, PostgreSQL, SQLite), Skripte und Excel-Dateien, neben den eingebauten Nodes der [Node-Referenz](user-guide/nodes/index.md).
- Betrieb: [Anmeldung](admin-guide/authentication.md) mit Rollen, eine [REST-API](admin-guide/api.md) und das [Kommandozeilenprogramm](admin-guide/cli.md), ein Audit-Log und das [Benutzerportal](admin-guide/portal.md) für Anwender, die nur Workflows ausführen.

### Update

Erste Version, es gibt nichts zu aktualisieren.
