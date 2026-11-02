# 30. Portal: permissions

Date: 2026-11-02 · Status: proposed

## Decision

- Version 1: every signed-in user sees all published workflows.
- Later: roles or groups from the login claim are assigned to workflows or groups in the backend (editable in the editor); empty means everybody. The portal sends the user's roles with each call and the backend filters. The portal never decides on its own.
