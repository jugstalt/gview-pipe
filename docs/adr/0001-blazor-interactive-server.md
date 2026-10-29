# 1. Blazor Web App with Interactive Server; headless engine in Core

Date: 2026-10-29 · Status: accepted

## Context

Workflows need server resources (file system, databases, HTTP without CORS, schedulers), and plugins must be loadable at runtime. The UI should also be embeddable in other Blazor apps such as gView.WebApps.

## Decision

- `gView.Pipe.Shell` is a Blazor Web App using Interactive Server render mode.
- The engine, plugin loader, scheduler and stores live in `gView.Pipe.Core`. Core has no Blazor or UI dependency. The Shell hosts the engine in-process for now.
- `gView.Pipe.Razor` components do not depend on a render mode. They only depend on `gView.Pipe.Abstraction`.
- Aspire (`gView.Pipe.AppHost`, `gView.Pipe.ServiceDefaults`) is set up from the start.

## Consequences

- A later worker or CLI host can reuse Core unchanged.
- WebAssembly hosting would need an API layer between UI and engine. That is not planned.
