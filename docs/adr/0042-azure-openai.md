# 42. Azure OpenAI for the AI node

Date: 2026-11-06 · Status: accepted

## Context

The AI node ([ADR 0041](0041-ai-node.md)) talks to Claude. Many organizations run language models in their own Azure tenant (Azure OpenAI), with the data under their Azure contract and in their region. The node was built behind `IPipeLlmProvider` for exactly this.

## Decision

- **The credential picks the provider.** `IPipeLlmProvider` names the credential type it serves and takes the data of the credential. The *API* field of the AI node accepts *Anthropic API* and *Azure OpenAI*; tasks, inputs and outputs stay the same.
- Credential **Azure OpenAI** (`848d5271-c4bc-4a38-bc50-cfc6538c01d4`): *Authentication* (*API key* or *Managed identity* through `DefaultAzureCredential`), *Endpoint* (`https://<resource>.openai.azure.com`), *Deployment*, *API version* (default `2025-01-01-preview`) and *API key* (secret, can come from a source). The endpoint may also be the whole URL of the deployment as the Azure portal shows it; deployment and API version are then taken from it.
- **Plain HTTP, no OpenAI SDK.** The provider calls `POST {endpoint}/openai/deployments/{deployment}/chat/completions?api-version=…` with `HttpClient`, so the API version is exactly the one configured. With a managed identity it sends a bearer token for `https://cognitiveservices.azure.com/.default` (`Azure.Identity`).
- **Mapping:**
  - the system text and the user content (text; images as `image_url` with a data URL) become the two messages;
  - the JSON schema of Classify and Extract goes as `response_format` `json_schema` with `strict: true`; the schemas of ADR 0041 meet its rules (all properties required, no additional properties);
  - *Max output tokens* is `max_completion_tokens`; a deployment that only knows `max_tokens` gets the request again with that;
  - `usage.prompt_tokens` and `completion_tokens` are the usage; the `model` of the answer is the model.
- **Differences, which the docs name:** *Model* of the node doesn't apply (the deployment decides), *Effort* is ignored, there are no fallbacks. A PDF attachment is an error ("PDF needs Claude"): chat completions don't take PDF documents. A content filter (`finish_reason` `content_filter`, or an answer `400` with code `content_filter`) and a `refusal` of the model are errors of the item; `length` is a cut-off answer.
- The URL and the body are built by public static functions, so they are tested without the service; the HTTP call itself is not.

## Consequences

- The plugin brings `Azure.Identity` along.
- Other OpenAI-compatible services (OpenAI itself, a local server) would be a further credential type with the same mapping and another URL and header; not part of this.
