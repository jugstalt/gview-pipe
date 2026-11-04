# 38. Email Trigger (IMAP)

Date: 2026-11-03 · Status: accepted

## Context

Mails that arrive in a mailbox are a frequent start of work: orders, reports, forms with attachments. Send Email can send them, but nothing starts a workflow when one comes in.

## Decision

- **Email Trigger** (Triggers, `916f55de-e11d-43cf-84c8-3afdebe6498d`) in the built-in plugin: MailKit is already part of it for Send Email. Credential **IMAP Server** (`c4a1127b-fac3-4956-bbf1-e6ef1bc04a62`): *Host*, *Port* (993), *Security* (*SSL on connect*, default; *StartTLS*; *None* for local test servers), *User name*, *Password* (secret, so it can come from a source, [ADR 0033](0033-external-secrets.md)). The certificate of the server is always checked; there is no option to ignore it.
- **Polling, no IDLE:** every *Check every (seconds)* (default 60, at least 10) the trigger connects, looks, and disconnects. IDLE needs an open connection per workflow that servers cut after about 30 minutes; one check a minute is usual for mail.
- **The server holds the state:** the trigger takes the **unread** mails (UNSEEN) of *Folder* (default `INBOX`), oldest first. After the run has been **started** it handles them as *After processing* says: *Mark as read* (default), *Move to folder* (*Target folder*, created if it is missing) or *Delete*. Mails that are unread when the workflow is activated count: they are work that was not done. A run that could not start (the workflow is busy and its queue full, [ADR 0016](0016-queued-runs.md)) leaves its mails unread, and they come with the next check.
- **Handled as soon as the run has started,** not when it has succeeded, as with files ([ADR 0036](0036-file-trigger.md)): a failed run would otherwise start again at every check. The error workflow reports the failure. If the mails can't be marked, moved or deleted (folder not allowed, connection lost), the trigger remembers the ones it has handed on, so the next check doesn't start them twice, and logs the error.
- **One run per check** with one item per mail, at most 50 per run; the rest follows with the next check. No mail, no run.
- **Filter:** *From contains* and *Subject contains*, searched on the server; empty = all. Mails that don't fit are not touched.
- **Item:** `messageId`, `uid`, `folder`, `from` (`name`, `address`), `to` and `cc` (lists of the same), `subject`, `date` (ISO 8601), `text`, `html`, `attachments` (the names) and `tooLarge`. With *Download attachments* (default on) each attachment is a binary attachment of the item (`attachment1`, `attachment2`, …); inline images are left out.
- **Size:** a mail larger than *Max message size (MB)* (default 25) is reported with its envelope only (`tooLarge: true`, no text or attachments) and then handled like the others.
- **Login:** user name and password only. Providers that allow only OAuth2 (XOAUTH2) are not supported yet.
- The connection is behind `IPipeMailConnector`, so the trigger is tested with a fake server; the MailKit adapter is thin and not tested against a real one.

## Consequences

- No state on disk and none to lose: a restart, or a second instance, sees the same unread mails. Two instances on one mailbox may both pick a mail; use one.
- Reading a mail may mark it as read on some servers even before the trigger does; with *Move* or *Delete* that makes no difference.
- Activating the workflow on a mailbox with many unread mails processes them 50 per check.
