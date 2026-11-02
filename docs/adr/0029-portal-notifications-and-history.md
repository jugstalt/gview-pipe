# 29. Portal: completion mail and history

Date: 2026-11-02 · Status: accepted

## Decision

- **Mail:** the user may tick *mail me when done* at the start (`notifyEmail` in the run request). A workflow of the admin sends it (`Pipe:Portal:NotifyWorkflowId`, like the error workflows of [ADR 0017](0017-failure-notifications.md)): it starts at its Execute Workflow Trigger with `{ to, user, workflow, execution: { id, status, startedAt, finishedAt, url? } }`, so the text and the SMTP credential are the admin's, and the backend needs no mail code. It is started from the run request, so a run that ends after a restart sends nothing. The link is built from `Pipe:Portal:PublicUrl`. Never results in the mail. No mail when the user waited and got the result. The address comes from the login (claim `email`, or `Email` of a forms user); the backend only checks the form of it, and trusts the portal as it does for the user name, so the portal sends only the address of the signed-in user. (First planned: the backend sends over an SMTP credential itself; the workflow is more flexible and fits the rest of the app.)
- **History:** a user sees only own runs. The result and the entered values are shown for `Pipe:Portal:HistoryDays` days (default 30), then only the status. The values are read from the run of the Form Trigger, so they exist only when the workflow saves item data; password fields are left out of what the portal shows. *Run again* opens the form with the values of a run, where the fields still exist. Result files (downloads) are not there yet, so their deletion after 7 days isn't either.

## Consequences

- The data of the run in the history of the editor still holds what a user typed, passwords included, unless *Save item data of executions* is off. Scrubbing password fields from stored runs is not done.
