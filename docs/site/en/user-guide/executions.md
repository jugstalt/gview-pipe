# Executions

An **execution** is one run of a workflow, started by the editor, a trigger or another workflow. gView.Pipe records every execution with what each node received, produced and logged.

## Running now

The start page lists every run in progress, whatever started it (the editor, Cron, a webhook or a sub-workflow). Each entry shows the workflow, its current node and the latest output line. The top bar shows **n running**.

Click a run to **watch** it in the editor, live. In the editor you can also **stop** it. Runs started by a webhook continue even when the caller disconnects, so stopping is the way to end one early.

!!! note
    The list shows runs of this app instance. A finished run stays visible for a minute.

## History

The workflow panel of the editor (select no node) lists the **executions** of the workflow, newest first, with status, start time, how it was started (Manual, Trigger for Cron and Webhook, Sub-workflow, Error workflow) and the duration. The last 50 are kept.

Click an execution to open it on the canvas: every node shows its state and counts, and you can open each node's **Run** tab to see the input, output and logs of that run. A note appears when the workflow was changed since the run, because some nodes may no longer exist.

| Status | Meaning |
|---|---|
| Success | The run finished. Individual nodes may have failed if they use *Continue* or *Error output*. |
| Error | A node failed with *Stop the execution*. The failed node is marked red. |
| Canceled | Someone stopped the run. |
| Running | Still in progress. |

Click the **x** next to the result chip above the canvas to clear the display and go back to the plain workflow.

## Sensitive data

Items of an execution are stored in the history, so they stay readable for everyone who can use the editor. Switch off **Save item data of executions** in the workflow panel if items contain sensitive data: then only statuses, counts and logs are stored. Secrets from webhook headers (`Authorization`, `Cookie`) never reach the items.

## Executions and restarts

Running executions live in the memory of the app. When the app restarts, they end. A Wait node that is waiting loses its run. Finished executions are kept in files and survive a restart.
