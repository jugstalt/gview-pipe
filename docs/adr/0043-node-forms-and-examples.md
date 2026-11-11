# 43. Node forms that explain themselves, and examples

Date: 2026-11-11 · Status: accepted

## Context

Users find the node panels hard to use. Read File, for example, shows *Folder* and *Path* without saying why both exist, it shows the *CSV delimiter* while *Binary* is selected, it doesn't say what *Include subfolders* does, and there is no example of what goes in and what comes out. The same problems show up in every node, because all forms are generated from the parameter classes (ADR 0004).

## Decision

The problems are solved once, in Abstraction and the generated forms, so built-in and external plugins use the same means. Nodes get no hand-written forms.

The default view must be clear to people who know spreadsheets but not JSON. Technical details go into a folded *Advanced* section.

### Fields

- **`[PipeParameter(ShowWhen = nameof(Format), ShowWhenValues = ["csv"])]`**: the field shows only while the other property has one of the values. Values are compared without case, so enum names work as written in C#; a bool is `"true"` or `"false"`. Exactly one property; anything that needs more is a sign that the node does too much. A hidden field keeps its value, nothing is deleted on save. Properties missing in the JSON count with the default of the parameter class.
- **`[PipeParameter(Advanced = true)]`**: the field is in the folded *Advanced* section at the end of the form.
- **`[PipeEnumValue(Label = …, Description = …)]`** on enum members: the label and a second line in the dropdown, and the text in the node reference.
- Values known only at run time (e.g. the released folders of Read File) are offered with the existing `[PipeOptions]` and `IPipeParameterOptions`; with exactly one value it is preselected in a new node.
- Every parameter, every enum value and every node has a description. The style: at most one line, what the field does, with an example; no repetition of the label and no internal words ("released by the host"). A test checks all built-in and external plugins of the repository.

### Examples

- **`[PipeNodeExamples]`** on the node class points to an embedded resource `<NodeClass>.examples.json` next to the class (or the name given). `Directory.Build.props` embeds every `*.examples.json`.
- The file is an array of examples: `title`, `description`, `parameters`, optional `given`, `input` (items), `output` (items of `main`, or an object of items per port), optional `ignore` (fields whose value changes, e.g. `modifiedAt`) and `verified` (default `true`).
- An item is its JSON object; attachments are in the reserved key `$binary`: `{ "data": { "fileName": "a.csv", "mimeType": "text/csv", "text": "…" } }`.
- `given` describes the world the example needs: `files` (path → text) in the folder `files`. Other keys belong to the plugin and its fake (e.g. `sftp`, `blobs`, `ai`).
- The node panel has a tab **Examples**: title, description, *given*, input and output in the same view as the Run tab (table, JSON or text), and a button **Use these parameters** that puts the parameters of the example into the node (undo works).
- The node reference shows the examples too.
- A test runs every example that is `verified` with the engine (Manual Trigger with the input → the node), with a temporary folder for `given.files` and the fakes of the plugins, and compares the output without the `ignore` fields. Triggers and nodes without a fake are `verified: false`, shown only; the test lists them. Every node has at least one example.

## Consequences

- Plugin authors write descriptions and examples with the node; the test fails if they are missing.
- Examples can't go stale: a verified example that no longer matches fails the build.
- `PipeNodeDescriptor.Examples` holds the examples; loading errors are warnings of the catalog, as for plugins.
- Older workflows are not touched: `ShowWhen` and `Advanced` change only the form.
