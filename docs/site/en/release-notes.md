# Release notes

What is new in each version, and what to do when you update. The complete list of changes is the [changelog](https://github.com/jugstalt/gview-pipe/blob/main/CHANGELOG.md) in the repository.

An update replaces the version folder of the [ZIP](admin-guide/installation.md) or the Docker image. The folders `data`, `files` and `scripts` and the folder `_config` stay as they are, so back them up before you update (see [Operations](admin-guide/operations.md)).

## 0.1.0

The first release.

### Highlights

- A visual editor for workflows made of nodes that pass JSON items along; the [editor](user-guide/editor.md) shows the input, output and log of every node.
- Triggers: manual, cron, webhook and form; webhooks can answer like a small API. See [Triggers and webhooks](user-guide/triggers.md).
- JavaScript [expressions](user-guide/expressions.md) in every parameter and a Code node, run in a sandbox.
- Variables and environments, groups, sub-workflows, error workflows and [failure notifications](user-guide/notifications.md).
- Plugins for databases (SQL Server, PostgreSQL, SQLite), scripts and Excel files, besides the built-in nodes of the [node reference](user-guide/nodes/index.md).
- Operation: [sign-in](admin-guide/authentication.md) with roles, a [REST API](admin-guide/api.md) and the [command line tool](admin-guide/cli.md), an audit log, and the [user portal](admin-guide/portal.md) for users who only run workflows.

### Upgrading

First release, nothing to upgrade from.
