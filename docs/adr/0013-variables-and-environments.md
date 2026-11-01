# 13. Variables and environments

Date: 2026-11-01 · Status: accepted

## Context

Script nodes take environment variables as parameters. Many script calls need the same values (`DATABASE_SERVER`, `DATABASE_INSTANCE`, …), so they are repeated in every node. To test a run against a test database, every node has to be changed by hand.

## Decision

### Environments

- An environment is a named scenario. The instance defines the list in `pipe.config` (`Pipe:Environments`); `DEFAULT` always exists and cannot be removed. The prototype adds `TEST`, `STAGE` and `PROD`.
- The instance runs in exactly one environment, `Pipe:Environment` (default `DEFAULT`). It is set by the administrator and is read-only in the UI: a server *is* PROD.
- Workflow groups and workflows have an environment or `inherit` (the default). The **effective environment** of a workflow is the first one set on workflow → group → instance.
- Moving a workflow to another group can change its effective environment; the UI shows the change ("TEST → PROD") and asks for confirmation.

### Variables

- Variables are defined on instance, group and workflow level, each with one value per environment (`DEFAULT` included).
- Instance variables come from `pipe.config` (`Pipe:Variables`, so `Pipe__Variables__…` works in Docker) and from a "Variables" page in the UI, stored in the data store. Values from the store win.
- The node level stays as it is: the plain environment parameters of the script nodes. They have no per-environment values and always win. No migration and no version change; node values may still contain `{{ }}`.
- **Resolution** for the active environment `E`: look up `E` on node → workflow → group → instance; if nothing is found, look up `DEFAULT` on the same chain. Environment comes before level, so a `DEFAULT` value lower in the chain never hides an explicit `PROD` value above it.
- Names match `^[A-Z_][A-Z0-9_]*$` and are stored in upper case, so they behave the same on Windows and Linux. The prefix `PIPE_` is reserved.
- A variable can be marked **secret**: stored encrypted with Data Protection (a value that can't be decrypted, e.g. in a copied workflow, becomes empty), shown as a password field, and replaced by `***` in node logs, error messages and script output. Item data a node produces from it is not scanned.
- The engine resolves the variables per execution:
  - Expressions read them as `$vars.NAME` (not `$env`, to avoid confusion with the process environment). An undefined variable is a runtime error naming the variable and the environment.
  - Run Script, Run Scripts Folder and Run Scripts Repo pass all defined variables to the process environment, overridden by the node's own parameters. Undefined ones are simply missing.
- Credentials are not affected; per-environment credentials are a separate topic.

### Environment of an execution

| Started by | Environment |
|---|---|
| Schedule | effective environment |
| Webhook | effective environment; header `X-Pipe-Environment` only if the workflow allows that environment (whitelist, empty by default). Not allowed → 400, never a silent fallback. No query parameter, so it doesn't end up in access logs. |
| Manual run in the editor | chosen in the editor, starting at the effective one. Temporary, not saved; a deviation is highlighted ("runs as TEST, saved: PROD"). |
| Sub-workflow, error workflow | inherited from the calling execution, so a test run never reaches PROD through Execute Workflow. |

- Every execution stores its environment; the executions list shows it and can filter by it.

### UI

- Workflow list and group tiles show a colored badge with the effective environment (PROD red, STAGE orange, TEST blue, DEFAULT grey); an inherited one is shown subdued with a tooltip naming its source.

## Consequences

- Workflows become portable between instances: the same workflow runs against test or production data depending on the server.
- Variables are a second place for configuration next to credentials; real secrets like passwords should still go to credentials.
- Executions, workflows and groups get new fields; old documents read as `inherit` without values.
