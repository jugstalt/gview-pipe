# Erste Schritte

## App starten

```bash
dotnet run --project src/gView.Pipe.Shell
```

Öffne die Adresse, die in der Konsole steht. Mit dem Profil `http` ist das `http://localhost:5240`, mit dem Profil `https` `https://localhost:7240`. Die Adresse einer installierten Instanz bekommst du von deinem Administrator (siehe [Administratorhandbuch](../admin-guide/index.md)).

## Die Beispiel-Workflows

Beim ersten Start mit leerem Speicher kopiert gView.Pipe mehrere Beispiel-Workflows in die Gruppe **Samples**:

| Workflow | Was er zeigt |
|---|---|
| Hello Pipe | Ein manueller Trigger, Set und Log. Der beste erste Workflow zum Öffnen. |
| Code Transform | JavaScript, Switch und Merge. |
| Item Tools | Split Out, Remove Duplicates, Sort, Limit und Aggregate. |
| Sub-workflow Demo, Price Calculator | Ein Workflow, der einen anderen pro Gruppe von Items aufruft. Fehler gehen an **Error Alerts**, einen Fehler-Workflow. |
| API Demo | Ein Webhook, der mit Respond-to-Webhook-Nodes antwortet. |
| Webhook Demo | Ein Webhook, der mit einer Begrüßung antwortet. Er ist inaktiv, bis du ihn einschaltest. |
| File Export | Schreibt Bestellungen als CSV und liest sie wieder ein. |

## Den ersten Workflow bauen

1. Lege auf der Startseite einen Workflow an und öffne ihn im Editor.
2. Füge aus der Palette einen Trigger-Node hinzu. Der **Manual Trigger** ist der einfachste: Der Workflow startet, wenn du ihn ausführst.
3. Füge weitere Nodes hinzu und verbinde den Ausgang eines Nodes mit dem Eingang des nächsten. Items fließen entlang der Verbindungen.
4. Klicke einen Node an, um seine Parameter im Seitenpanel zu bearbeiten. In jedem Textfeld ist `{{ … }}` ein JavaScript-Ausdruck, der gegen das aktuelle Item ausgewertet wird, zum Beispiel `{{ $json.total * 1.2 }}`.
5. Führe den Workflow aus. Jeder Node zeigt die Items, die er bekommen hat, die Items, die er erzeugt hat, und die Logzeilen, die er geschrieben hat.

## Trigger brauchen einen aktiven Workflow

Ein manueller Lauf funktioniert jederzeit. **Cron**- und **Webhook**-Trigger feuern nur, solange der Workflow **Active** ist: Schalte ihn mit dem Schalter **Active** im Editor ein. Siehe [Trigger und Webhooks](triggers.md).

## Wie geht es weiter

- [Trigger und Webhooks](triggers.md): Workflows von außen starten und dem Aufrufer antworten.
- [Fehlerbehandlung](error-handling.md): Wiederholungen, Fehlerausgänge und Fehler-Workflows.
