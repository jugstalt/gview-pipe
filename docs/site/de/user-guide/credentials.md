# Credentials

Ein **Credential** enthält die Zugangsdaten, die ein Node braucht: ein Passwort, ein Token, einen SMTP-Server, einen Connection String. Du legst es einmal an und wählst es in Nodes aus. Das Geheimnis taucht nie im Workflow selbst auf.

Öffne **Credentials** in der oberen Leiste, um sie zu sehen.

![Die Credential-Liste](../../images/credentials.png)

## Ein Credential anlegen

1. Klicke auf **New credential**.
2. Wähle den **Typ** und gib einen **Namen** ein.
3. Fülle die Felder aus. **Geheime** Felder (Passwörter, Tokens) werden verdeckt angezeigt.
4. Speichere.

Die Liste zeigt Name, Typ und **Id** jedes Credentials. Die Id speichert der Workflow, und die Id trägt ein Administrator in die Konfiguration ein (zum Beispiel für einen Git-Server).

!!! important
    Nach dem Speichern werden geheime Felder **nie wieder angezeigt**. Um ein Geheimnis zu ändern, gibst du es neu ein. Credentials werden auf dem Server verschlüsselt gespeichert.

## Typen

| Typ | Benutzt von | Felder |
|---|---|---|
| Basic Auth | HTTP Request, Webhook Trigger, Git-Server | Benutzername, Passwort |
| Bearer Token | HTTP Request, Webhook Trigger, Git-Server | Token |
| API Key Header | HTTP Request, Webhook Trigger | Header-Name, Wert |
| OAuth2 Client Credentials | HTTP Request | Token-URL, Client-Id, Client-Secret, Scope, Audience, wie die Client-Daten gesendet werden (Header oder Body) |
| Connection String | SQL | Provider (`sqlserver`, `postgres` oder `sqlite`), Connection String |
| SFTP Server | SFTP | Host, Port, Benutzername, Passwort und/oder privater Schlüssel (mit Passphrase), Fingerabdruck des Host-Keys |
| SMTP | Send Email | Host, Port, Sicherheit (auto, SSL on connect, StartTLS, none), Benutzername, Passwort, Absenderadresse |

Die genauen Felder stehen bei den [Credential-Typen](nodes/credential-types.md) der Node-Referenz.

## Ein Credential verwenden

Parameter, die ein Credential brauchen, zeigen eine Auswahl mit den Credentials der passenden Typen. Wähle eines aus. Die Hinweise des Nodes sagen dir, wenn es fehlt.

- **HTTP Request:** Das Credential authentifiziert den Aufruf (Basic, Bearer oder ein API-Key-Header).
- **Webhook Trigger:** Aufrufer müssen den passenden `Authorization`-Header (oder API-Key-Header) senden. Ohne Credential ist der Webhook öffentlich.
- **SQL:** der Connection String und der Provider.
- **Send Email:** der SMTP-Server und das Login.
- **Skript-Nodes:** Die Felder des Credentials gehen als Umgebungsvariablen `PIPE_CREDENTIAL_<FELD>` an das Skript. Die Werte werden in Ausgabe und Logs maskiert.

## OAuth2 Client Credentials

Viele APIs geben kurzlebige Access Tokens aus. Mit diesem Credential holt HTTP Request ein Token von der *Token-URL* (Grant `client_credentials`) und sendet es als `Authorization: Bearer …`. Das Token bleibt im Speicher und wird eine Minute vor Ablauf erneuert, sodass viele Requests und Läufe sich ein Token teilen; ändert man das Credential, gibt es ein neues. Antwortet die API mit `401`, wird das Token verworfen und der nächste Versuch (ein Retry des Nodes, der nächste Lauf) holt ein neues. Unterstützt wird nur dieser Grant, nicht das Login eines Benutzers im Browser. Für das Token eines Benutzers nimm ein Bearer-Token-Credential.

## Ein Secret aus einer anderen Quelle nehmen

Ein Secret-Feld hat einen Schlüssel-Knopf. Er übernimmt den Wert aus einer Umgebungsvariablen, Azure Key Vault oder dem Vault von IdentityServer.NET, statt ihn im Credential zu speichern. Nach dem Speichern erscheinen Quelle und Name; *Check* sagt, ob das Secret lesbar ist. Siehe [Secrets](../admin-guide/secrets.md).

## Gut zu wissen

- Ein Credential gehört zur ganzen App, nicht zu einem Workflow. Wer Workflows bearbeiten kann, kann jedes Credential *benutzen*, aber seine Geheimnisse nicht lesen.
- Workflow-Dateien enthalten nur die Credential-Id. Du kannst sie weitergeben, ohne Geheimnisse weiterzugeben; die andere Installation muss ein Credential mit derselben Id anlegen oder ein anderes auswählen.
- Beim Umzug einer Installation kopierst du die Ordner `credentials` **und** `keys` zusammen (siehe [Administratorhandbuch](../admin-guide/operations.md#backup)).
