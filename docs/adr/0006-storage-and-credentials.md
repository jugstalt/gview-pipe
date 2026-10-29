# 6. File storage; credentials encrypted with Data Protection

Date: 2026-10-29 · Status: accepted

## Decision

- Stores sit behind `IPipeWorkflowStore`, `IPipeExecutionStore` and `IPipeCredentialStore`.
- `AddPipeCore()` alone registers in-memory stores, for headless use and tests.
- `AddPipeFileStore()` writes JSON files below the data root:
  - `workflows/{id}.json`, versionable with Git
  - `executions/{workflowId}/{startedAt}_{id}.json`
  - `credentials/{id}.json`
- Unknown JSON properties survive a round trip (`[JsonExtensionData]`).
- Writes go to a temp file first and are then moved into place.
- Ids are restricted to `[A-Za-z0-9_-]`, because they are used as file names.
- Credentials are a concept of their own: a POCO credential type, referenced by id. Their data is encrypted with ASP.NET Data Protection (purpose `gView.Pipe.Credentials.v1`).
- By default the Data Protection keys are persisted to `keys/`. Hosts that configure Data Protection themselves set `PersistDataProtectionKeys = false`.

- The data root is runtime state and is never committed. Sample workflows live in `/samples/workflows`.
  - The Shell copies them to its output folder.
  - The file store copies them into `workflows/` when that folder contains no workflow yet (`SeedWorkflowsPath`).

## Consequences

- Anyone with access to both `keys/` and `credentials/` can decrypt the credentials. Production setups should protect the keys, for example with DPAPI, certificates or Azure Key Vault.
- The Shell's whole `data/` folder is excluded from Git. Workflows you want to share become samples.
- A database store can be added later behind the same interfaces.
