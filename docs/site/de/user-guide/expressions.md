# Items und Ausdrücke

## Items

Daten fließen als **Items** durch einen Workflow. Ein Item ist ein JSON-Objekt, plus optionale binäre Anhänge (Dateien). Eine Verbindung trägt eine Liste von Items, und ein Node bekommt alle Items seiner Eingänge auf einmal.

```json
{ "id": 1, "customer": "Ada", "total": 120, "country": "AT" }
```

Die meisten Nodes bearbeiten jedes Item einzeln: Der Set-Node ergänzt Felder in jedem Item, If schickt jedes Item nach `true` oder `false`. Einige Nodes arbeiten auf der ganzen Liste: Aggregate sammelt viele Items in einem, Split Out macht aus einem Item viele, Limit und Sort verändern die Liste.

Items werden auf dem Weg in einen Node kopiert, parallele Zweige können sich also nicht gegenseitig die Daten ändern.

## Ausdrücke

Jeder Textparameter kann **Ausdrücke** in doppelten geschweiften Klammern enthalten. Der Inhalt ist JavaScript:

```
Hallo {{ $json.customer }}, deine Summe ist {{ $json.total * 1.2 }} EUR
```

- Besteht ein Parameter aus **einem** Ausdruck, behält er den Typ des Ergebnisses: `{{ $json.total * 1.2 }}` ist eine Zahl, `{{ $json.tags }}` ein Array.
- Steht Text um die Ausdrücke herum, ist das Ergebnis **Text**.
- Ausdrücke laufen einmal pro Item, mit diesem Item als `$json`.

### Variablen

| Variable | Bedeutung |
|---|---|
| `$json` | Das JSON des aktuellen Items. |
| `$itemIndex` | Die Position des aktuellen Items in der Liste (ab 0). |
| `$input.first()`, `$input.all()` | Das erste Item oder alle Items der Eingabe des Nodes. |
| `$node["Name"].json` | Das erste Item, das der Node *Name* erzeugt hat. `.all` ist die Liste aller seiner Items. |
| `$workflow` | Id und Name des Workflows. |
| `$execution` | Id des aktuellen Laufs. |
| `$now` | Die aktuelle Zeit als ISO-Text. |
| `$vars.NAME` | Eine Variable des Workflows, seiner Gruppe oder der Instanz, siehe [Variablen und Environments](variables.md). |
| `$environment` | Das Environment des Laufs, zum Beispiel `PROD`. |

Es gibt kein `$env`: Die Umgebungsvariablen des Servers enthalten oft Geheimnisse. Für gemeinsame Werte nimmst du [Variablen](variables.md), für Geheimnisse [Credentials](credentials.md).

### Beispiele

| Ziel | Ausdruck |
|---|---|
| Ein verschachteltes Feld | `{{ $json.address.city }}` |
| Sicherer Zugriff, wenn das Feld fehlen kann | `{{ $json.address?.city ?? 'unknown' }}` |
| Rechnen | `{{ Math.round($json.total * 1.2 * 100) / 100 }}` |
| Text verbinden | `{{ $json.first + ' ' + $json.last }}` |
| Bedingung | `{{ $json.total > 100 ? 'big' : 'small' }}` |
| Daten eines früheren Nodes | `{{ $node["Products"].json.name }}` |
| Body eines Webhooks | `{{ $json.body?.name ?? 'stranger' }}` |
| Items zählen | `{{ $input.all().length }}` |

!!! warning
    Der Zugriff auf ein Feld von etwas, das es nicht gibt, ist ein **Fehler**, wie in JavaScript: `$json.missing.deeper` schlägt fehl, wenn `missing` fehlt. Nimm `?.`: `$json.missing?.deeper`.

Werte werden in den Ausdruck kopiert: Wenn du `$json` im Ausdruck änderst, ändert sich das Item nicht. Ausdrücke können weder Netzwerk noch Dateien benutzen und werden nach einer Sekunde abgebrochen.

### Der Editor hilft

Im Tab **Run** eines Nodes klickst du ein Feld der Eingabe an, um den passenden Ausdruck in den zuletzt bearbeiteten Parameter einzufügen. So kommst du am schnellsten zum richtigen Pfad.

## Der Code-Node

Für alles, was größer als ein Ausdruck ist, führt der **Code**-Node JavaScript aus. Er hat zwei Modi:

- **All items:** Dein Code bekommt `items` (ein Array mit dem JSON aller Eingangs-Items) und gibt ein Array von Objekten zurück, aus dem die Ausgangs-Items werden.
- **Each item:** Dein Code läuft einmal pro Item mit `$json` und gibt ein Objekt zurück, oder `null`, um das Item zu verwerfen.

```javascript
// Modus: All items
const orders = items.filter(order => order.total > 100);
console.log(`${orders.length} big orders`);
return orders.map(order => ({ ...order, big: true }));
```

```javascript
// Modus: Each item
if ($json.country === 'DE') return null;
return { ...$json, vat: $json.total * 0.2 };
```

- `console.log` schreibt in die **Logs** des Nodes.
- Die Variablen der Ausdrücke (`$node`, `$workflow`, `$now`, …) stehen zur Verfügung.
- Der Code wird nicht nach `{{ }}` durchsucht; schreibe einfaches JavaScript.
- Ein Code-Node darf bis zu 30 Sekunden laufen. Er kann weder Netzwerk noch Dateien nutzen: Dafür gibt es die Nodes HTTP Request und Read File.
- Anhänge eines Items bleiben erhalten, wenn die Zahl der Items gleich bleibt.

## Anhänge

Dateien (zum Beispiel aus einem Webhook-Upload oder dem Node Read File) wandern als **binäre Anhänge** unter einem Namen mit dem Item. Sie sind nicht Teil des JSON. Nodes wie Write File und Send Email können sie benutzen. Die Bytes der Anhänge gibt es nur, solange die Execution läuft; in der Historie siehst du, dass ein Anhang existierte, kannst ihn danach aber nicht herunterladen.
