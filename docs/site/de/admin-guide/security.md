# Sicherheit

Diese Seite zeigt, was die App schützt und was du selbst schützen musst.

## Anmeldung

Ohne Konfiguration (`Pipe:Auth:Mode` = `none`) stehen der Editor und alle seine Funktionen jedem offen, der die Adresse erreicht. Dann:

- binde die App an `localhost` oder ein internes Netz, und
- setze einen Reverse Proxy oder ein VPN davor, wenn Leute von anderen Rechnern zugreifen.

Sobald andere die App erreichen, schalte eine Anmeldung ein: feste Benutzer (`forms`) oder einen Identity Provider (`oidc`), siehe [Anmeldung](authentication.md). Wer angemeldet ist, darf alles; Rollen innerhalb der App gibt es nicht. Betreibe die App mit Anmeldung über https, damit Passwörter und das Sitzungs-Cookie verschlüsselt übertragen werden.

## Was ein Workflow kann

Ein Workflow kann Webadressen aufrufen (HTTP Request), Mails senden, Dateien lesen und schreiben, SQL ausführen und mit dem Skript-Plugin Skripte starten. Du begrenzt das durch das, was du freigibst:

| Fähigkeit | Begrenzt durch |
|---|---|
| Read File / Write File | Nur die Ordner in `Pipe:FileRoots`, angesprochen über ihren Namen. |
| Skripte | Nur Dateien aus `Pipe:Scripts:ScriptRoots` oder von den konfigurierten Git-Servern. Keine Inline-Skripte, keine freien Pfade. |
| SQL | Nur über ein Connection-String-Credential. Werte sind immer Parameter; der SQL-Text wird nie als Ausdruck ausgewertet. |
| JavaScript (Ausdrücke, Code-Node) | Eine Sandbox-Engine mit Zeit- und Speichergrenzen. |

Wer Workflows bearbeiten kann, kann alles nutzen, was du freigibst. Behandle den Zugriff auf den Editor als **Administratorzugriff** auf die freigegebenen Ordner, Server und Credentials.

## Skripte laufen mit dem Konto der App

Starte die App unter einem Konto (oder in einem Container), das nur tun darf, was deine Skripte brauchen. Starte sie nicht als Administrator oder root.

## Credentials

- Credentials werden mit ASP.NET Data Protection verschlüsselt gespeichert. Geheime Felder werden nach dem Speichern nie wieder angezeigt.
- Nodes verweisen per Id auf Credentials. Sie sind nicht Teil des Workflow-JSON, du kannst Workflow-Dateien also weitergeben.
- Die Schlüssel liegen in `data/keys/`. **Wer Zugriff auf `keys/` und `credentials/` hat, kann die Credentials entschlüsseln.** Beschränke den Zugriff auf den Ordner `data`. Für den Produktivbetrieb kann ein Entwickler Data Protection so konfigurieren, dass die Schlüssel mit DPAPI, einem Zertifikat oder einem Key Vault geschützt werden.
- Die Header `authorization`, `proxy-authorization` und `cookie` von Webhook-Aufrufen (und der API-Key-Header) werden nicht in die Items kopiert und gelangen so nie in die Execution-Historie. Credential-Werte werden in Skriptausgaben und Logs maskiert.

## Webhooks

Gib jedem Webhook, der von außen erreichbar ist, ein Credential. Siehe [Betrieb](operations.md#webhooks-im-betrieb).

## Plugins

Ein Plugin läuft mit den vollen Rechten der App. Installiere nur Plugins aus Quellen, denen du vertraust.

## Updates

Spiele Updates regelmäßig ein. Die Runtime und die Bibliotheken von Dritten bekommen Sicherheitskorrekturen.
