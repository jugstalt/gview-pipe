# 39. SFTP Trigger

Date: 2026-11-04 · Status: accepted

## Context

The SFTP node ([ADR 0037](0037-sftp.md)) works on files of a server, but nothing starts a workflow when a partner puts one there. A Cron Trigger with List would have to remember what it has seen. The File Trigger ([ADR 0036](0036-file-trigger.md)) and the Email Trigger ([ADR 0038](0038-email-trigger.md)) solved the same for a local folder and a mailbox.

## Decision

- **SFTP Trigger** (Triggers) in the plugin `gView.Pipe.Plugins.Sftp`, with the credential and the connection of the SFTP node (`ISftpConnector`). Settings: *Server* (credential), *Path* (folder on the server), *File pattern* (`*`, capitals don't matter), *Include subfolders* (off), *After processing*, *Target folder*, *Events*, *Check every (seconds)* (default 60, at least 10).
- **Polling:** it connects at every check, lists the folder and disconnects. The first check is made as soon as the workflow is activated. Files that are not files (folders), and names ending in `.part` or `.tmp`, are never reported: uploaders often write under such a name and rename when done, as the SFTP node does.
- **A file is complete** when its size and time were the same in two checks in a row (a server can't tell us about locks). A file that never gets stable is not reported.
- **After processing** says what happens to a file once the run has started, and with it what counts as new:
  - *Move to folder* (default; *Target folder* required, created if missing) and *Delete*: **every** file counts, also those that were there before. The server holds the state, so a restart loses nothing. A name that is taken in the target gets `_2`, `_3`, … before its extension.
  - *Nothing*: the files of the first successful check are the baseline and trigger nothing; later new files are reported, and with *Events* = *Created or changed* also changed ones. The trigger keeps no state on disk, so files that arrive while the host is down are in the baseline after a restart.
- **One run per check** with one item per file, at most 100; the rest follows with the next check. The item is that of the SFTP node's List (`fileName`, `path`, `size`, `modifiedAt`, `isDirectory` = false) plus `event` (`created` or `changed`). The SFTP node's *Download* fetches the content with `{{ $json.path }}`.
- **The files are handled as soon as the run has started,** not when it succeeds, as for mail ([ADR 0038](0038-email-trigger.md)): a failing run would otherwise start again at every check, and the error workflow reports it. A run that could not start (the workflow is busy and its queue full, [ADR 0016](0016-queued-runs.md)) leaves the files where they are; they are offered again. If a file can't be moved or deleted, the trigger remembers it and doesn't start it twice, and logs the error.
- A server that can't be reached, or a host key that doesn't match, is logged and the next check tries again; the state stays as it was.

## Consequences

- With *Move* or *Delete* the account needs write permission on the folder. For read-only accounts, *Nothing* is the way.
- Two instances on one server folder may both pick a file; use one.
- A file that appears and is removed by someone else between two checks is never reported.
