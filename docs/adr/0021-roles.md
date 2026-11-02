# 21. Roles

Date: 2026-11-02 · Status: accepted

## Context

Every signed-in user may do everything ([ADR 0014](0014-authentication.md)): change and delete workflows, see credentials, create API keys. Teams that share an instance need users who only look, or only run workflows, or only work on their own group.

## Decision

### Roles

Four roles, each with the rights of the ones before:

| Role | May |
|---|---|
| viewer | see workflows, executions, groups |
| operator | run, stop and replay; activate and deactivate workflows |
| editor | create, change and delete workflows and groups; pin data; variables of groups and workflows |
| admin | credentials, instance variables, API keys |

- A user has a role for the instance and may have another one per group, which replaces it for the workflows of that group. Credentials, instance variables and API keys only look at the instance role.
- Without a login (mode *none*) everyone is admin.

### Where roles come from

They come from `auth.config` and are worked out on every check, not stored in the login cookie: after a change and a restart of the app they apply without a new login.

- **Forms:** per user `Role` and `GroupRoles` (`{ "billing": "editor" }`).
- **OIDC:** `Oidc:RoleMappings` maps values of the role claim to `role` or `role@group` (`{ "pipe-admins": "admin", "pipe-billing": "editor@billing" }`). The highest match wins; a group gets at least the instance role.
- A user without an entry gets `Pipe:Auth:DefaultRole` (default *admin*, so existing installations keep working; the docs recommend *viewer* once roles are set up).

### Checks

- `IPipeAuthorization` (Abstraction, scoped per circuit) gives the roles of the current user (`PipeUserRoles`): `Role(groupId)` and `Can(permission, groupId)`. Core registers full access; `AddPipeAuthentication()` replaces it with the claims-based one.
- The Razor components check on the server before they save, delete or run, and say "Not allowed"; buttons are disabled with a tooltip, navigation entries without the right are hidden. The stores don't check: engine, triggers and the API use them too.
- viewer and operator open workflows read only (no palette, no saving, parameters disabled); operator may run, stop and replay.
- The account menu shows the role, and the groups with another one.
- API keys keep their own rights ([ADR 0020](0020-rest-api.md)); webhooks and triggers are not affected.

## Consequences

- Hosts that embed the components without `AddPipeAuthentication()` give everyone full access, as before.
- Roles can't be edited in the UI: users live in the configuration or the identity provider.
