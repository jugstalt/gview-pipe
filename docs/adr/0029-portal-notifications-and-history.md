# 29. Portal: completion mail and history

Date: 2026-11-02 · Status: proposed

## Decision

- **Mail:** the user may tick *mail me when done* at the start. The backend sends it with an SMTP credential from its configuration, because only it knows reliably when a run ended. The address comes with the run (`notifyEmail`). Content: workflow, status and a link into the portal, never results.
- **History:** a user sees only own runs, 30 days with parameters and result, then only the status. Result files are deleted after 7 days (both configurable). Password fields are never stored. *Run again* opens the form with the old values where fields still exist. The admin history is unaffected.
