# AI

The **AI** node (plugin `gView.Pipe.Plugins.AI`) asks a language model about the items: it answers a prompt, puts items into categories, reads fields out of text or documents, or summarizes. The model is Claude, through the Anthropic API, or a deployment of [Azure OpenAI](#azure-openai) in your own Azure tenant.

!!! warning "Data leaves the server"
    The input of the node goes to the Anthropic API or to your Azure OpenAI deployment. Installing the plugin is the decision to allow that; without the plugin no workflow can send data to a language model. Think about what is in the items before you send them, for example personal data.

## Set it up

1. Create an API key in the Claude Console. The account of the key pays for the calls.
2. Create an **Anthropic API** [credential](credentials.md) with the key. It can come from a [secret source](../admin-guide/secrets.md).
3. Add the **AI** node and select the credential.

## Tasks

| Task | What you set | Result in the item |
|---|---|---|
| Prompt | *Prompt*: what the model should do, with `{{ }}` expressions | `text` |
| Classify | *Categories*: a list | `category` (always one of the list) and `reason` (one short sentence) |
| Extract | *Fields*: name and meaning of each field | `data`, an object with these fields |
| Summarize | *Length*: short, medium or long | `summary` |

For **Extract**, start the meaning of a field with `string:`, `number:`, `boolean:` or `date:` to set its type, for example `number: total amount with VAT`; without a prefix it is a string. Dates come as `YYYY-MM-DD`. A field the input doesn't hold is `null`. Classify and Extract use structured outputs: the answer always has exactly this form, so later nodes can rely on it.

Every result item also has `usage`: the `model` that answered and the `inputTokens` and `outputTokens` of the call.

## Input

*Input* says what goes to the model:

- **Whole item** (default): the JSON of the item.
- **Field**: the value of the expression in *Field*, for example `{{ $json.body }}`.
- **Attachment**: a binary attachment of the item, named in *Attachment*. PDFs go as documents, PNG, JPEG, GIF and WebP as images, text and JSON as text; other types are an error. One request takes at most 32 MB, so an attachment can have about 24 MB.
- **None**: only the prompt (task Prompt only).

*Run* is **Each item** (default: one call per item, one after the other, with *Pause between calls*) or **Once for all items** (all items as one JSON array in one call; the result is one new item). Attachments need Each item.

A typical flow: [Email Trigger](triggers.md#email-trigger) → **AI** (Extract, Input Attachment `attachment1`, fields `invoice`, `number: total`, `date: date`) → SQL or Write File.

## Model and cost

- *Model*: Claude Opus 5.5 (default), Claude Sonnet 5.5 or Claude Haiku 5.5. Haiku is the cheapest and often enough for Classify on many items; try it on a few items and compare.
- *Effort*: how deeply the model thinks and how many tokens it uses. *Auto* is low for Classify and Extract and medium for Prompt and Summarize.
- *Max output tokens*: 0 takes a default for the task. An answer cut off at the limit is an error.
- The tokens are also counted in the metric `gview.pipe.ai.tokens` (see [Operations](../admin-guide/operations.md#metrics)).

If the model declines a request, the item fails with the reason; *On error* of the node applies. With Opus and Sonnet the request asks the API to try a model it recommends for that case first.

## Data is not instructions

Each task has a fixed instruction, and the data of the item is passed in a marked block that the model is told to treat as material, not as instructions. *Instructions* adds your own text, for example the language of the answer. This makes it less likely that text in a mail or document steers the model, but it can't rule it out. **Treat what the model returns as data:** check it with If or Filter before it sends mails, deletes files or does anything else that matters.

Input and answer are part of the node run and stay in the [execution history](executions.md) unless *Save execution data* of the workflow is off.

## Azure OpenAI

Instead of Claude, the node can use a model deployment of **Azure OpenAI** in your own Azure tenant. The data then goes to that deployment, under your Azure contract and in its region. The credential decides: with an **Azure OpenAI** credential in *API* the node talks to Azure; tasks, inputs and results stay the same.

The credential has:

- *Endpoint*: `https://<resource>.openai.azure.com`. You can also paste the whole URL of the deployment as the Azure portal shows it (`…/openai/deployments/<name>/chat/completions?api-version=…`); deployment and API version are then taken from it.
- *Deployment*: the name of the model deployment.
- *API version*: default `2025-01-01-preview`. Classify and Extract need one that knows structured outputs (`2024-08-01-preview` or later).
- *Authentication*: **API key** (one of the keys of the resource; can come from a [secret source](../admin-guide/secrets.md)) or **Managed identity** (the managed identity of the app in Azure, your developer login on your machine; it needs the role *Cognitive Services OpenAI User*).

What is different from Claude:

- *Model* doesn't apply: the deployment decides the model. The `usage.model` of the result names it.
- *Effort* is ignored, and there are no fallbacks.
- **PDF attachments** are an error: chat completions of Azure OpenAI don't take PDF documents. Images, text and JSON work.
- If the content filter of Azure stops a request or an answer, the item fails like a declined request of Claude.
