# 36. File Trigger

Date: 2026-11-03 · Status: accepted

## Context

Workflows start on a schedule, a webhook, a form or by hand. A frequent case is missing: a file appears in a folder and the workflow processes it. With a Cron Trigger the workflow would have to look itself and remember what it saw.

## Decision

- **File Trigger** (Triggers): starts the workflow when new (optionally changed) files appear in a folder released by the host (`Pipe:FileRoots`, as for Read File and Write File).
- **Polling, no `FileSystemWatcher`.** Every *Check every* seconds (default 10, at least 1) the trigger compares a snapshot of the folder (path, size, modified time) with the last one. This works on network shares and container volumes, has no event storms or buffer overflows, and answers "is the file complete?" by itself: a file is reported when size and time were the same in two checks in a row and the file can be opened for reading without being shared for writing. A file that never gets stable (a growing log) is not reported; the log says so after 30 checks.
- **What triggers:** the setting *Events* is *Created* (default) or *Created or changed*. Files that exist when the workflow is activated, and after a restart of the host, count as known and trigger nothing. The trigger keeps no state on disk: what it has reported is forgotten at a restart. A file that disappears and comes back is new. To not see a file again, the workflow moves or deletes it after processing.
- **One run per check** with all new files as items, at most 100 per run; the rest follows with the next check. A check that finds nothing starts nothing.
- **Items** hold information only: `folder`, `path` (relative to the folder, with `/`), `name`, `size`, `modified` (ISO 8601) and `event` (`created` or `changed`). Read File reads the content with `{{ $json.path }}` as its path.
- **Settings:** *Folder*, *Path* (a subfolder; empty = the folder), *File pattern* (default `*`), *Include subfolders* (default off), *Events*, *Check every (seconds)*.
- **If a run can't start** (the workflow is busy and its queue full, [ADR 0016](0016-queued-runs.md)), the files stay waiting and are offered again with the next check. If the folder can't be read, the known state stays as it was and the log warns once; so a folder that comes back doesn't report everything again.

## Consequences

- Reports are delayed by up to two intervals: one check to see the file, one more to see it unchanged.
- The folder name and path are checked when the workflow is activated, so a wrong folder shows as a problem of the activation.
