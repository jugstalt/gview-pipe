# 41. AI node

Date: 2026-11-06 · Status: accepted

## Context

Workflows often need judgement on text: sort mails into categories, read an invoice number out of a PDF, summarize tickets. Expressions and the Code node can't do that. A language model can, but the data of the items then goes to an external service.

## Decision

### Plugin and provider

- A separate plugin **`gView.Pipe.Plugins.AI`**. Without it no workflow can send data to a language model, as without the Scripts plugin no workflow can run scripts.
- Behind a small interface `IPipeLlmProvider` (in the plugin): one request with system text, user content (text, document, image) and an optional JSON schema; the answer is text or a JSON object, the stop reason and the token usage. Claude (Anthropic API, official C# SDK `Anthropic`) is the only provider now; others, e.g. a local model, can follow behind the same interface.
- Credential **Anthropic API** (`f906caca-2310-49a7-b9f2-d7b4cc23575a`): *API key* (secret field, can come from a source, [ADR 0033](0033-external-secrets.md)).

### Node

Node **AI** (`3fcdd4cb-dbb4-49f0-a892-dd04dce6f916`, Category AI) with *Task*:

- **Prompt:** the author's prompt (expressions allowed); the answer as text in `text`.
- **Classify:** *Categories* (a list); the answer in `category`, always one of the list, and a short `reason`.
- **Extract:** *Fields* (name, type `string`, `number`, `boolean` or `date`, description); the answer as object in `data`, matching the schema. Fields the input doesn't hold are `null`.
- **Summarize:** a summary in `summary`, with *Length* (`short`, `medium`, `long`).

Classify and Extract use structured outputs (`output_config.format` with a JSON schema), so the answer always has the form later nodes expect.

*Input* says what goes to the model: **Field** (an expression, e.g. `{{ $json.text }}`), **Whole item** (the JSON of the item) or **Attachment** (a binary attachment: PDF as document, PNG/JPEG/GIF/WebP as image, `text/*` and JSON as text; anything else is an error). A request larger than 32 MB is refused before it is sent.

*Run* is **Each item** (default; one call per item, one after the other, with *Pause between calls*, [ADR 0034](0034-batches-and-pauses.md)) or **Once for all items** (all items as one JSON document, one output item). Calls don't run in parallel.

The output item is the input item with the answer fields and `usage` (`model`, `inputTokens`, `outputTokens`). With *Once for all items* it is a new item.

### Instructions and data

- Each task has a fixed system text in the code, which always says that the content of the data block is material, not instructions. *Instructions* (optional) adds the author's own text to it.
- The data of the item is in a marked block (`<data>…</data>`), apart from what the author writes.
- This lowers the chance that text in the data steers the model, but can't rule it out. The docs say so: what a model returns is data, not trusted control; check it (If, Filter) before it triggers anything.

### Model, cost and refusals

- *Model*: Claude Opus 5.5 (`claude-opus-5-5`, default), Claude Sonnet 5.5 or Claude Haiku 5.5; the author chooses per node.
- *Effort* (`low`, `medium`, `high`): default `low` for Classify and Extract, `medium` for Prompt and Summarize (*Auto*). *Max output tokens* with a default per task.
- A refusal of the model (`stop_reason` `refusal`) and an answer cut off at *Max output tokens* are errors of the item; *On error* of the node applies. For Opus and Sonnet the request asks for server-side fallbacks (`fallbacks: "default"`, beta `server-side-fallback-2026-07-01`), so a declined request is tried on the model Anthropic recommends for that case. Haiku has no server-side fallback.
- The token usage goes into the item and into the metric `gview.pipe.ai.tokens` (counter; tags `model`, `direction` = input/output, workflow; [ADR 0032](0032-metrics.md)).

### History and tests

- Prompt input and answer are part of the node run like any output and are kept in the execution history unless *Save execution data* is off.
- The node is tested with a fake provider; the Claude adapter is thin and not tested automatically (it needs a key and costs money). No sample workflow: the plugin tests run all samples.

## Consequences

- Data of the items leaves the host. Installing the plugin is the decision to allow that; the API key decides which account pays.
- Long or many items cost money; the usage in the items and the metric make it visible.
