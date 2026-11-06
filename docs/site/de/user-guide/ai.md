# KI

Der Node **AI** (Plugin `gView.Pipe.Plugins.AI`) fragt ein Sprachmodell zu den Items: Er beantwortet einen Prompt, ordnet Items Kategorien zu, liest Felder aus Text oder Dokumenten oder fasst zusammen. Das Modell ist Claude, über die Anthropic API, oder ein Deployment von [Azure OpenAI](#azure-openai) in deinem eigenen Azure-Mandanten.

!!! warning "Daten verlassen den Server"
    Die Eingabe des Nodes geht an die Anthropic API oder an dein Azure-OpenAI-Deployment. Wer das Plugin installiert, entscheidet sich dafür; ohne das Plugin kann kein Workflow Daten an ein Sprachmodell schicken. Überlege, was in den Items steht, bevor du sie schickst, zum Beispiel personenbezogene Daten.

## Einrichten

1. Lege in der Claude Console einen API-Key an. Das Konto des Keys bezahlt die Aufrufe.
2. Lege ein [Credential](credentials.md) **Anthropic API** mit dem Key an. Er kann aus einer [Secret-Quelle](../admin-guide/secrets.md) kommen.
3. Füge den Node **AI** ein und wähle das Credential.

## Aufgaben

| Task | Was du einstellst | Ergebnis im Item |
|---|---|---|
| Prompt | *Prompt*: was das Modell tun soll, mit `{{ }}`-Ausdrücken | `text` |
| Classify | *Categories*: eine Liste | `category` (immer eine aus der Liste) und `reason` (ein kurzer Satz) |
| Extract | *Fields*: Name und Bedeutung jedes Feldes | `data`, ein Objekt mit diesen Feldern |
| Summarize | *Length*: short, medium oder long | `summary` |

Bei **Extract** beginnt die Bedeutung eines Feldes mit `string:`, `number:`, `boolean:` oder `date:`, um den Typ festzulegen, zum Beispiel `number: Gesamtbetrag mit MwSt.`; ohne Präfix ist es ein Text. Datumswerte kommen als `YYYY-MM-DD`. Ein Feld, das die Eingabe nicht enthält, ist `null`. Classify und Extract nutzen strukturierte Ausgaben: Die Antwort hat immer genau diese Form, nachfolgende Nodes können sich darauf verlassen.

Jedes Ergebnis-Item hat außerdem `usage`: das `model`, das geantwortet hat, und die `inputTokens` und `outputTokens` des Aufrufs.

## Eingabe

*Input* bestimmt, was an das Modell geht:

- **Whole item** (Standard): das JSON des Items.
- **Field**: der Wert des Ausdrucks in *Field*, zum Beispiel `{{ $json.body }}`.
- **Attachment**: ein Binär-Anhang des Items, benannt in *Attachment*. PDFs gehen als Dokument, PNG, JPEG, GIF und WebP als Bild, Text und JSON als Text; andere Typen sind ein Fehler. Eine Anfrage darf höchstens 32 MB groß sein, ein Anhang also etwa 24 MB.
- **None**: nur der Prompt (nur bei Task Prompt).

*Run* ist **Each item** (Standard: ein Aufruf pro Item, nacheinander, mit *Pause between calls*) oder **Once for all items** (alle Items als ein JSON-Array in einem Aufruf; das Ergebnis ist ein neues Item). Anhänge brauchen Each item.

Ein typischer Ablauf: [Email Trigger](triggers.md#email-trigger) → **AI** (Extract, Input Attachment `attachment1`, Felder `invoice`, `number: total`, `date: date`) → SQL oder Write File.

## Modell und Kosten

- *Model*: Claude Opus 5.5 (Standard), Claude Sonnet 5.5 oder Claude Haiku 5.5. Haiku ist am günstigsten und reicht oft für Classify auf vielen Items; probiere es an einigen Items aus und vergleiche.
- *Effort*: wie gründlich das Modell nachdenkt und wie viele Tokens es dafür braucht. *Auto* ist low für Classify und Extract und medium für Prompt und Summarize.
- *Max output tokens*: 0 nimmt einen Standard für die Aufgabe. Eine Antwort, die an der Grenze abgeschnitten wird, ist ein Fehler.
- Die Tokens werden zusätzlich in der Metrik `gview.pipe.ai.tokens` gezählt (siehe [Betrieb](../admin-guide/operations.md#metriken)).

Lehnt das Modell eine Anfrage ab, schlägt das Item mit dem Grund fehl; es gilt *On error* des Nodes. Bei Opus und Sonnet bittet die Anfrage die API, vorher ein Modell zu versuchen, das sie für diesen Fall empfiehlt.

## Daten sind keine Anweisungen

Jede Aufgabe hat eine feste Anweisung, und die Daten des Items gehen in einem markierten Block mit, den das Modell als Material behandeln soll, nicht als Anweisung. *Instructions* ergänzt eigenen Text, etwa die Sprache der Antwort. Das macht es weniger wahrscheinlich, dass Text in einer Mail oder einem Dokument das Modell steuert, kann es aber nicht ausschließen. **Behandle, was das Modell zurückgibt, als Daten:** Prüfe es mit If oder Filter, bevor es Mails verschickt, Dateien löscht oder sonst etwas Wichtiges auslöst.

Eingabe und Antwort gehören zum Node-Lauf und bleiben im [Ausführungsverlauf](executions.md), außer *Save execution data* des Workflows ist aus.

## Azure OpenAI

Statt Claude kann der Node ein Modell-Deployment von **Azure OpenAI** in deinem eigenen Azure-Mandanten nutzen. Die Daten gehen dann an dieses Deployment, unter eurem Azure-Vertrag und in dessen Region. Das Credential entscheidet: Mit einem Credential **Azure OpenAI** in *API* spricht der Node mit Azure; Aufgaben, Eingaben und Ergebnisse bleiben gleich.

Das Credential hat:

- *Endpoint*: `https://<ressource>.openai.azure.com`. Du kannst auch die ganze URL des Deployments einfügen, wie das Azure-Portal sie zeigt (`…/openai/deployments/<name>/chat/completions?api-version=…`); Deployment und API-Version werden dann daraus genommen.
- *Deployment*: der Name des Modell-Deployments.
- *API version*: Standard `2025-01-01-preview`. Classify und Extract brauchen eine, die strukturierte Ausgaben kennt (`2024-08-01-preview` oder neuer).
- *Authentication*: **API key** (einer der Schlüssel der Ressource; kann aus einer [Secret-Quelle](../admin-guide/secrets.md) kommen) oder **Managed identity** (die Managed Identity der App in Azure, auf deinem Rechner dein Entwickler-Login; sie braucht die Rolle *Cognitive Services OpenAI User*).

Was anders ist als bei Claude:

- *Model* gilt nicht: Das Deployment bestimmt das Modell. `usage.model` im Ergebnis nennt es.
- *Effort* wird ignoriert, und es gibt keine Fallbacks.
- **PDF-Anhänge** sind ein Fehler: Die Chat Completions von Azure OpenAI nehmen keine PDF-Dokumente an. Bilder, Text und JSON funktionieren.
- Hält der Inhaltsfilter von Azure eine Anfrage oder Antwort an, schlägt das Item fehl wie eine abgelehnte Anfrage bei Claude.
