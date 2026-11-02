# 18. Data nodes

Date: 2026-11-02 · Status: accepted

## Context

Read File and Write File know binary, text, JSON and CSV, but only for files in released folders. A CSV uploaded to a webhook, a mail attachment or an HTTP response can't be turned into items without a detour through the file system. XML, Excel, dates, hashes and ZIP files need a Code node or are not possible at all.

## Decision

### File formats

- `IPipeFileFormat` (Abstraction, `Files`) reads a stream into JSON objects and writes JSON objects into a stream. Formats are registered as services; plugins add their own in their `IPipePluginStartup`. `PipeFileFormatOptions` carries the settings a format may use (delimiter, encoding, header row, sheet, XML elements).
- Built in (`gView.Pipe.Plugins`): CSV, JSON, XML, Text. Read File and Write File keep their parameters and use the same implementations.
- Excel comes from the external plugin `gView.Pipe.Plugins.Office` (MiniExcel), so the built-ins stay free of it.

### Extract from File and Convert to File

- **Extract from File** reads the attachment `data` (configurable) of each item in a format. The result is one item per row or object (default), or the input item with all rows in a field (`rows`).
- **Convert to File** writes items in a format into an attachment (`data`, file name with expressions). Tables (CSV, Excel) always make one file of all items, with the union of their fields as columns; Excel can put the items into one sheet per value of a field. JSON and XML make one file of all items (an array, or elements under one root) or one file per item; Text writes a field of the items.
- CSV: the delimiter is detected from the first row unless set; text is read as UTF-8 unless a BOM or the encoding setting says otherwise (`windows-1252`, `iso-8859-1`, `utf-16`).
- XML ↔ JSON: attributes become `@name`, text next to attributes or child elements `#text`, repeated elements arrays; *Always array* names elements that are arrays even when they occur once. Namespace prefixes stay in the names. *Item element* makes each element of that name an item.

### Date & Time, Crypto, Compression

- **Date & Time**: now, format, parse, add, subtract, difference, convert time zone, start of a unit. Patterns follow .NET (`yyyy-MM-dd HH:mm`), cultures are .NET culture names, time zones IANA or Windows ids. The result goes into a target field.
- **Crypto**: hash (MD5, SHA-1, SHA-256, SHA-512) of a field or an attachment, HMAC (SHA-256, SHA-512) with a secret from an expression (e.g. a secret variable), UUID, Base64 encode and decode; output hex or Base64.
- **Compression**: ZIP the attachments of each item or of all items, unzip into one item per entry (default) or all entries as attachments of one item, GZip and GUnzip one attachment. Unzipping stops above a total size (default 500 MB), against ZIP bombs; entry paths are kept as text and never touch the file system.

## Consequences

- The node ids are new and fixed in `PipeBuiltIn.cs`; Read File and Write File keep theirs and their parameters.
- Other plugins can add formats (e.g. GeoJSON) without new nodes.
- Excel needs the Office plugin installed; without it the format isn't offered.
