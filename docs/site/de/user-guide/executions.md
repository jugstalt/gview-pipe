# Executions

Eine **Execution** ist ein Lauf eines Workflows, gestartet vom Editor, von einem Trigger oder von einem anderen Workflow. gView.Pipe zeichnet jede Execution auf, mit dem, was jeder Node bekommen, erzeugt und geloggt hat.

## Läuft gerade

Die Startseite listet jeden laufenden Lauf, egal wer ihn gestartet hat (der Editor, Cron, ein Webhook oder ein Sub-Workflow). Jeder Eintrag zeigt den Workflow, seinen aktuellen Node und die letzte Ausgabezeile. Die obere Leiste zeigt **n running**.

Klicke einen Lauf an, um ihn im Editor **live zu verfolgen**. Im Editor kannst du ihn auch **stoppen**. Läufe, die ein Webhook gestartet hat, laufen weiter, auch wenn der Aufrufer die Verbindung trennt; Stoppen ist also der Weg, einen früh zu beenden.

!!! note
    Die Liste zeigt Läufe dieser App-Instanz. Ein beendeter Lauf bleibt eine Minute sichtbar.

## Historie

Das Workflow-Panel des Editors (ohne gewählten Node) listet die **Executions** des Workflows, die neueste zuerst, mit Status, Startzeit, wie er gestartet wurde (Manual, Trigger für Cron und Webhook, Sub-Workflow, Fehler-Workflow) und der Dauer. Die letzten 50 bleiben erhalten.

Klicke eine Execution an, um sie auf der Arbeitsfläche zu öffnen: Jeder Node zeigt Zustand und Zähler, und im Tab **Run** jedes Nodes siehst du Eingabe, Ausgabe und Logs dieses Laufs. Ein Hinweis erscheint, wenn der Workflow seit dem Lauf geändert wurde, weil manche Nodes dann nicht mehr existieren.

| Status | Bedeutung |
|---|---|
| Success | Der Lauf ist durchgelaufen. Einzelne Nodes können fehlgeschlagen sein, wenn sie *Continue* oder *Error output* nutzen. |
| Error | Ein Node ist mit *Stop the execution* fehlgeschlagen. Der fehlgeschlagene Node ist rot markiert. |
| Canceled | Jemand hat den Lauf gestoppt. |
| Running | Läuft noch. |

Klicke auf das **x** neben dem Ergebnis über der Arbeitsfläche, um die Anzeige zu löschen und zum einfachen Workflow zurückzukehren.

## Sensible Daten

Die Items einer Execution werden in der Historie gespeichert und bleiben damit für alle lesbar, die den Editor nutzen können. Schalte **Save item data of executions** im Workflow-Panel aus, wenn Items sensible Daten enthalten: Dann werden nur Status, Zähler und Logs gespeichert. Geheimnisse aus Webhook-Headern (`Authorization`, `Cookie`) gelangen nie in die Items.

## Executions und Neustarts

Laufende Executions liegen im Speicher der App. Beim Neustart enden sie. Ein wartender Wait-Node verliert seinen Lauf. Beendete Executions liegen in Dateien und überstehen einen Neustart.
