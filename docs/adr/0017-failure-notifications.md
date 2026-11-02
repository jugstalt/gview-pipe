# 17. Failure notifications

Date: 2026-11-02 · Status: accepted

## Context

A workflow can name an error workflow (`PipeWorkflowSettings.ErrorWorkflowId`, see [ADR 0005](0005-execution-semantics.md)): when a triggered run fails, it starts at its Error Trigger with `{ workflow, execution, node }`. It has to be set on every workflow, the item has no link to the failed run, a workflow failing every minute sends a message every minute, and a failing sub-workflow reports the same failure twice (itself and its caller). The only sample writes to the log.

## Decision

### Inherited error workflow

- The error workflow is set on the workflow, the group (`PipeWorkflowGroup.ErrorWorkflowId`) and the instance (`Pipe:ErrorWorkflowId` in `pipe.config`). The first one set on workflow → group → instance applies, like the environment ([ADR 0013](0013-variables-and-environments.md)).
- On the workflow it is *inherit* (null, the default), a workflow id, or *none* (`PipeErrorWorkflows.None`), which stops the inheritance. Workflows that had no error workflow now inherit; nothing changes unless a group or the instance sets one.
- `IPipeErrorWorkflows.Resolve` returns the effective one and where it comes from, so the UI can show "Inherit (Error Mail from group 'Billing')".
- An error workflow never starts for its own failures.

### What starts it

- Failed runs in mode `Trigger` and `Replay`. Manual runs don't: the editor shows the failure.
- Runs in mode `SubWorkflow` no longer start it. The caller fails too and reports it; its item names the sub-workflow (`subWorkflow: { id, name, executionId, node }`). A failure the caller catches (`OnError` Continue or Error output) is not reported; that is what catching means.

### The item

- `{ workflow, execution, node, subWorkflow?, suppressed }`. With `Pipe:PublicUrl` set, `execution.url` links to the failed run in the editor.

### Throttling

- The Error Trigger has `Throttle minutes` (default 15, 0 = off): per failed workflow, at most one run of the error workflow in that time. Suppressed failures are counted and reported with the next one as `suppressed`.
- The count lives in memory and starts over after a restart.
- Existing error workflows get the default too, so they send fewer messages than before.

### Samples

- *Error Mail* (Send Email to `ALERT_MAIL_TO`), *Error Teams* (HTTP Request with an Adaptive Card to `ALERT_TEAMS_URL`), *Error Slack* (HTTP Request to `ALERT_SLACK_URL`), next to *Error Alerts* (log only). No node of its own for notifications: Teams and Slack are one POST each.

## Consequences

- The runner keeps the last failed sub-workflow run per calling execution until the caller ends.
- A "back to normal" message after a failure would need the last state per workflow; it is not part of this.
