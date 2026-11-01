# Rezepte

Die Beispiel-Workflows, die mit gView.Pipe kommen, sind zugleich Rezepte. Jeder löst eine kleine Aufgabe und zeigt ein paar Nodes. Sie liegen in einer leeren Installation in der Gruppe **Samples**, und du kannst die JSON-Dateien hier herunterladen.

!!! note "Eine heruntergeladene Datei verwenden"
    Einen Import-Knopf gibt es noch nicht. Kopiere die Datei in den Ordner `workflows` im Datenordner der App (siehe [Administratorhandbuch](../admin-guide/operations.md#backup)) und lade die Workflow-Liste neu; starte die App neu, falls der Workflow nicht erscheint. Der Dateiname ist die Id des Workflows. Workflows, die andere Workflows aufrufen (`Execute Workflow`, Fehler-Workflows), verweisen per Id auf sie. Behalte also die Ids.

## Hello Pipe

*Manual Trigger → Set → If → Log.* [Herunterladen](../../downloads/workflows/hello-pipe.json)

Der erste Workflow zum Öffnen. **Set Greeting** ergänzt Felder im Item (`greeting` nutzt einen Ausdruck), **If** (`{{ $json.score }}` größer als `10`) schickt das Item nach `true` oder `false`, und einer von zwei **Log**-Nodes schreibt das Ergebnis. Führe ihn aus und sieh dir den Tab **Run** jedes Nodes an. Der Zweig ohne Items wird übersprungen (grau).

## Code Transform

*Code → Switch → Set → Merge → Log.* [Herunterladen](../../downloads/workflows/code-transform.json)

Ein **Code**-Node erzeugt vier Bestellungen. **Switch** leitet sie nach `{{ $json.country }}` weiter: Bestellungen aus AT gehen an einen **Set**-Node (20 % MwSt.), Bestellungen aus DE an einen anderen (19 %), und **Merge** führt die beiden Zweige wieder zusammen. Bestellungen aus anderen Ländern verlassen den Node über den Ausgang `fallback`, der hier nicht verbunden ist, sie werden also verworfen. Ein gutes Muster für "je nach Art des Items anders behandeln".

## Item Tools

*Split Out → Remove Duplicates → Sort → Limit → Aggregate.* [Herunterladen](../../downloads/workflows/item-tools.json)

Bestellungen enthalten eine Liste von Positionen. **Split Out** macht aus jeder Position ein eigenes Item, **Remove Duplicates** behält ein Item je `sku`, **Sort** sortiert nach Preis (absteigend), **Limit** behält die ersten drei, und **Aggregate** sammelt die SKUs in einem Item.

## Sub-workflow Demo und Price Calculator

*Execute Workflow pro Gruppe.* [Sub-workflow Demo](../../downloads/workflows/sub-workflow-demo.json) · [Price Calculator](../../downloads/workflows/price-calculator.json) · [Error Alerts](../../downloads/workflows/error-alerts.json)

**Price Calculator** beginnt mit einem **Execute Workflow Trigger** und addiert die MwSt. **Sub-workflow Demo** ruft ihn mit **Execute Workflow** im Modus *batches* auf (zwei Items je Aufruf). So baust du eine Schleife: Rufe einen Sub-Workflow pro Item oder pro Gruppe auf. Die Demo nennt **Error Alerts** als Fehler-Workflow: Schlägt der Lauf fehl, schreibt Error Alerts den Fehler ins Log.

## API Demo

*Webhook → If → Respond to Webhook.* [Herunterladen](../../downloads/workflows/api-demo.json)

Eine kleine API: `POST /webhook/api/greet` mit `{ "name": "Ada" }` antwortet mit `200` und einer Begrüßung, ohne Namen mit `400` und einem Fehlertext. Der Trigger wartet auf einen **Respond to Webhook**-Node (*Using respond node*). Aktiviere den Workflow zuerst.

```bash
curl -i -X POST http://localhost:5240/webhook/api/greet -H "Content-Type: application/json" -d '{"name":"Ada"}'
```

Siehe [Trigger und Webhooks](triggers.md#respond-to-webhook).

## Webhook Demo

*Webhook → Set.* [Herunterladen](../../downloads/workflows/webhook-demo.json)

Der kürzeste Webhook: `POST /webhook/demo` antwortet mit einer Begrüßung. Der Trigger steht auf *When finished*, der Aufrufer bekommt also die Items des letzten Nodes. Der Workflow ist inaktiv, bis du ihn einschaltest.

## File Export

*Code → Write File → Read File.* [Herunterladen](../../downloads/workflows/file-export.json)

Schreibt Bestellungen als CSV-Datei in den freigegebenen Ordner `files` (`exports/orders-<Datum>.csv`) und liest die Datei als Items wieder ein. Der Ordnername `files` muss vom Administrator freigegeben sein (`Pipe:FileRoots`), siehe [Administratorhandbuch](../admin-guide/configuration.md).

## Selbst bauen

- **Eine API nach Zeitplan aufrufen:** Cron Trigger → HTTP Request → Code oder Set → Send Email.
- **Auf einen Webhook reagieren und antworten:** Webhook Trigger (*Using respond node*) → deine Nodes → Respond to Webhook, mit einem Fehlerausgang für Fehlschläge ([Fehlerbehandlung](error-handling.md#einen-webhook-nach-einem-fehler-beantworten)).
- **Nachts ein Skript ausführen und bei einem Fehler eine Mail bekommen:** Cron Trigger → Run Script, mit einem Fehler-Workflow, der die Mail sendet.
- **Zeilen zwischen Datenbanken kopieren:** Cron Trigger → SQL (select) → SQL (insert, einmal pro Item).
