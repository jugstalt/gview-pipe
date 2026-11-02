# 30. Portal: permissions

Date: 2026-11-02 · Status: accepted

## Context

The portal ([ADR 0027](0027-portal-backend.md)) first showed every published workflow to every signed-in user. Organizations want to show some services to some people only.

## Decision

- **Portal roles** are plain names that the login of the portal gives a user (`hr`, `finance`, …). They have nothing to do with the roles viewer to admin of the editor ([ADR 0021](0021-roles.md)).
- **Where they come from:** with OpenID Connect the claims of the role claim type of the provider (`Oidc.RoleClaimType`, default `role`); with forms the new `Roles` of the user in `Portal:Auth:Users`, which become the claims `role`.
- **What they allow:** a published workflow may name roles (`Settings.Portal.Roles`), a group may name roles for its workflows that don't (`PortalRoles`). The roles of the workflow decide; without them those of its group; without both everybody may. A user needs one of the roles; case is ignored. This is `PipePortalAccess.Allows`.
- **Who decides:** the backend. The portal sends the roles of the user with every call in `X-Pipe-On-Behalf-Of-Roles` (each role percent-encoded, separated by commas), and the backend filters the list, and answers *not found* for starting, looking at or stopping a run of a workflow the roles don't allow. The portal never decides on its own.
- **Runs:** a run of a workflow the user may no longer see isn't listed or shown anymore, so removing a role takes the workflow away completely.
- **Editor:** *Only these roles* in the portal section of the workflow settings, *Portal roles* in the group dialog.

## Consequences

- The backend trusts the portal to name the roles of the right user, exactly as it trusts it for the name: a stolen portal key can claim any role. A key limited to groups still limits what the portal reaches at all.
- Roles come from the login, so changes there apply at the next sign-in.
- There is no list of users or roles in the backend; the editor takes whatever is typed.
