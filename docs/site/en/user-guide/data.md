# Files and data

Files travel through a workflow as **attachments** of items (see [Items and expressions](expressions.md)). A webhook upload, a mail attachment, an HTTP response or Read File gives you one; Send Email, Write File and HTTP Request take one. The nodes on this page turn attachments into items and back, and work with dates, hashes and archives. The sample **Data Tools** uses all of them ([Recipes](recipes.md#data-tools)).

![The sample Data Tools after a run: the CSV read back from the unzipped attachment](../../images/data-tools.png)

## Extract from File

Reads the attachment `data` (setting *Attachment name* under *Advanced*) of each item in a format and makes items of it.

| Format | Becomes | Settings |
|---|---|---|
| CSV | one item per row; values are text | *Header row*, *Delimiter* (empty: detected from the first row), *Encoding* |
| Excel | one item per row; numbers stay numbers, dates become ISO text | *Header row*, *Sheet* (empty: the first). Needs the Office plugin. |
| JSON | an object is one item, an array one item per element | *Encoding* |
| XML | the whole document as one item, or one item per *Item element* | *Item element*, *Always array*, *Encoding* |
| Text | one item with the content in `text` | *Encoding* |

*Output* **Field** keeps the input item and puts all rows into one field (`rows`) instead. Use it when the item carries more than the file, for example the sender of a mail.

Text is read as UTF-8 unless the file starts with a byte order mark or *Encoding* says otherwise, for example `windows-1252` for CSV files saved by an older Excel.

### XML as items

XML elements become JSON like this:

```xml
<order id="7" xmlns:x="urn:x"><line>Pen</line><line>Ink</line><x:note>fast</x:note></order>
```

```json
{ "@id": "7", "@xmlns:x": "urn:x", "line": ["Pen", "Ink"], "x:note": "fast" }
```

Attributes start with `@`, text next to attributes or elements is in `#text`, repeated elements become arrays. An element that occurs once is a single value: list it in *Always array* (e.g. `line`) when your expressions expect an array.

## Convert to File

Writes items into an attachment (`data`, file name in *File name*, with expressions).

- **CSV** and **Excel** always make one file of all items. The columns are all fields of the items in the order they appear; nested objects are written as JSON. Excel can put the items on one sheet per value of a field (*Sheet per field*, e.g. `region`).
- **JSON** writes an array of all items, or with *Files* = *Each item* each item as an object.
- **XML** writes all items as *Item element* below *Root element* (`<items><item>…</item></items>`), or one document per item. An item read by Extract from File (`{ "order": { … } }`) is written back with the same root.
- **Text** writes the field *Text field* (`text`) of the items, one per line.

## Date & Time

Works on a date and writes the result into *Target field*.

| Operation | Result |
|---|---|
| Now | the current time, in *Time zone* if set |
| Format | the date as text in *Pattern* (e.g. `dd.MM.yyyy HH:mm`), in *Culture* (e.g. `de-DE` for German month names) and *Time zone* |
| Parse | text in *Pattern* as ISO date; text without an offset is in *Time zone* |
| Add, Subtract | *Amount* of *Unit*; months and years only in whole numbers |
| Difference | from *Date* to *Until* (empty: now) in *Unit*; months and years are counted as whole calendar units |
| Convert time zone | the same moment in *Time zone* |
| Start of | the start of the day, week (Monday), month, … in *Time zone* |

Dates are read as ISO 8601 (`2026-11-02T08:30:00+01:00`), common text forms, or Unix seconds or milliseconds. Results are ISO 8601 with offset, so the next node reads them without a pattern. Time zones are IANA ids like `Europe/Vienna` or Windows ids. Patterns follow .NET: `yyyy` year, `MM` month, `dd` day, `HH` hour, `mm` minute, `dddd` weekday name, `MMMM` month name.

## Crypto

| Operation | Result |
|---|---|
| Hash | SHA-256, SHA-512, SHA-1 or MD5 of *Value* or of an attachment, as hex or Base64 |
| HMAC | a signature of *Value* or an attachment with *Secret*, e.g. to check a webhook signature |
| UUID | a new random id |
| Base64 encode / decode | *Value* or an attachment as Base64, or Base64 back to text |

Take the HMAC secret from a secret variable (`{{ $vars.WEBHOOK_SECRET }}`, see [Variables and environments](variables.md)), so it isn't stored in the workflow. MD5 and SHA-1 are fine for checksums, not for security.

## Compression

| Operation | Result |
|---|---|
| Zip | the attachments of each item, or of all items (*Zip* = *All items*), in one ZIP archive (*Archive name*) |
| Unzip | one item per file of the archive, with the file in `data` and `fileName`, `path`, `size`; or all files as attachments of the input item |
| GZip / GUnzip | compresses or decompresses one attachment |

Unpacking stops with an error when the content gets larger than *Max size* (500 MB), so a small archive can't fill the server. Archives are never unpacked to disk: paths in the archive are only text in the items.

## SFTP

The **SFTP** node (plugin `gView.Pipe.Plugins.Sftp`) lists, downloads, uploads, deletes and renames files on an SFTP server. It runs once per input item over one connection, and the paths accept expressions.

First create an **SFTP Server** [credential](credentials.md): host, port, user name, a password and/or a private key, and the **fingerprint of the server's host key** (`SHA256:…`). The fingerprint is required: a server with another one is refused, and the error shows the fingerprint it presented, so you can check it with the owner of the server and enter it. Password, key and passphrase can come from a [secret source](../admin-guide/secrets.md).

| Operation | What it does |
|---|---|
| List | One item per file in *Path* (`fileName`, `path`, `size`, `modifiedAt`, `isDirectory`). *File pattern* filters (`*.csv`), *Include subfolders* goes deeper, *Include folders* adds folders. More than 10000 entries are an error. |
| Download | Gets the file in *Path* (one file, no pattern) and attaches it to the item, by default as `data`. Files larger than *Max file size (MB)* (100) are refused before they are loaded. |
| Upload | Writes the attachment of the item to *Path*. A file that exists is an error unless *Overwrite* is on; a missing folder is an error unless *Create folders* is on. |
| Delete | Deletes the file in *Path*. |
| Rename | Renames or moves *Path* to *New path*. A target that exists is an error unless *Overwrite* is on. |

An upload goes to `<name>.part` first and is renamed when it is complete, so a reader on the server never sees half a file. With *Overwrite*, the old file is deleted just before that rename.

Local files are not touched by this node: content travels as attachments. Combine it with Read File and Write File to fetch a file from the server into a released folder, or to send one from there. A typical flow: **List** → **Download** (with `{{ $json.path }}` as *Path*) → process → **Rename** into an archive folder on the server.

## Azure Blob

The **Azure Blob** node (plugin `gView.Pipe.Plugins.AzureStorage`) lists, downloads, uploads, deletes and renames blobs in Azure Blob Storage, with the same operations and item fields as the [SFTP](#sftp) node plus `container`, `contentType` and `etag`. A blob has a *Container* and a *Path*; folders are the parts of the path before a `/`.

First create an **Azure Storage** [credential](credentials.md). *Authentication* is one of:

- **Connection string**: from the access keys of the account. For the emulator Azurite it is `UseDevelopmentStorage=true`.
- **Managed identity**: only the *Account URL* (`https://<account>.blob.core.windows.net`). The app signs in with its managed identity in Azure, or with your developer login (Azure CLI, Visual Studio) on your machine. The identity needs the role *Storage Blob Data Contributor*, or *Reader* for List and Download.
- **SAS token**: the *Account URL* and a shared access signature.

The connection string and the SAS token can come from a [secret source](../admin-guide/secrets.md).

| Operation | What it does |
|---|---|
| List | One item per blob in the folder *Path* (empty: the whole container). *File pattern* filters the names, *Include subfolders* goes deeper. More than 10000 blobs are an error. |
| Download | Gets the blob in *Path* and attaches it to the item (default `data`). Blobs larger than *Max file size (MB)* (100) are refused before they are loaded. |
| Upload | Writes the attachment of the item to *Path*. A blob that exists is an error unless *Overwrite* is on; the server checks that in the same request. A missing container is an error unless *Create container* is on. |
| Delete | Deletes the blob in *Path*. |
| Rename | Moves *Path* to *New path*, also into *New container*. Blob Storage has no rename: the blob is copied on the server and then deleted, so a large blob takes the time of a copy. |

A blob becomes visible only when it is completely uploaded, so there is no temporary name.
