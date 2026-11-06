# 40. Azure Blob Storage

Date: 2026-11-06 · Status: accepted

## Context

Besides SFTP ([ADR 0037](0037-sftp.md), [ADR 0039](0039-sftp-trigger.md)), files are often exchanged through cloud storage. Azure Blob Storage fits the Azure SDK the secrets plugin already uses ([ADR 0033](0033-external-secrets.md)).

## Decision

- A separate plugin **`gView.Pipe.Plugins.AzureStorage`** with `Azure.Storage.Blobs` and `Azure.Identity`. Only blobs; Files, Queues and Tables are not part of it.
- Credential **Azure Storage** (`8d237e62-b6d3-4f25-8450-0171baefa0b7`) with *Authentication*:
  - *Connection string* (secret field), also for the emulator Azurite;
  - *Managed identity*: *Account URL* only, signed in with `DefaultAzureCredential` (managed identity in Azure, the developer login locally);
  - *SAS token* (secret field) with *Account URL*.
  The secrets can come from a source.
- Node **Azure Blob** (`eff26ad3-e0ba-4a53-9e43-c9fbfa5066b2`, Category Files) with the operations of the SFTP node, once per input item, paths with expressions. *Container* and *Path* (blob name; folders are prefixes with `/`).
  - **List:** *Path* is the prefix (a folder), *File pattern* on the name, *Include subfolders*. More than 10000 blobs are an error.
  - **Download:** one blob as attachment, refused above *Max file size (MB)* (100) before it is loaded.
  - **Upload:** the attachment of the item. A blob that exists is an error unless *Overwrite*; the check is made by the server in the same request (`If-None-Match: *`), so two runs can't overwrite each other unnoticed. *Create container* creates a missing container. No `.part` name: a blob is visible only when it is complete.
  - **Delete.**
  - **Rename:** copy on the server, then delete, as Blob Storage has no rename; also into another container (*New container*, empty = the same). A target that exists is an error unless *Overwrite*.
  The items carry the fields of the SFTP node (`fileName`, `path`, `size`, `modifiedAt`) plus `container`, `contentType` and `etag`, so a workflow can move between the two with few changes.
- Trigger **Azure Blob Trigger** (`b5ed26a2-4701-4514-b8ec-64a7f6639766`) as the SFTP Trigger: *After processing* = *Move to folder* (default; target prefix, also in another container), *Delete* or *Nothing* (baseline, only new blobs, optionally changed ones). A blob is reported at the **first** check that sees it, because it is visible only when complete; there is no `.part` rule. One run per check, at most 100 blobs. Default every 60 seconds, at least 10. Event Grid (push) is not part of it: it needs an address reachable from Azure and setup there.
- The SDK is behind `IPipeBlobConnector`, so node and trigger are tested with a fake; the adapter is thin. The AppHost gets `ADD_AZURITE` to try it by hand.

## Consequences

- Rename and *Move to folder* copy the data on the server; a large blob takes the time of a copy, not of a rename.
- With *Managed identity* the identity needs the role *Storage Blob Data Contributor* (or *Reader* for List and Download only).
