# 7. JavaScript (Jint) for expressions and the Code node

Date: 2026-10-29 · Status: accepted (supersedes the path-only templating of ADR 0005)

## Context

Path-only expressions (`$json.a.b`) could not compute anything. Even `{{ $json.price * 1.2 }}` was impossible. Users of workflow tools expect JavaScript, as in n8n.

## Decision

- **One evaluator.** `PipeJavaScriptEngine` (Jint) implements `IPipeExpressionEvaluator` and replaces the path evaluator. The old syntax is valid JavaScript and keeps working. The rest stays as before: a template consisting of a single expression keeps its JSON type, and mixed text becomes a string.
- **Variables:**
  - `$json`, `$itemIndex`, `$workflow`, `$execution` and `$now` (ISO text)
  - `$input.all()` and `$input.first()`
  - `$node["Name"].json` for the first item and `.all` for all items. `$node` is a lazy proxy.
  - **No `$env`**: environment variables often hold secrets.
- **Scripts:** `IPipeScriptRunner` (Abstraction) runs a JavaScript function body for nodes, so plugins don't need to reference Jint.
  - The Code node has two modes. "All items" gets `items` and returns an array of objects. "Each item" gets `$json` and returns an object, or `null` to drop the item.
  - `console.log` goes to the node logger.
  - Code fields are marked `[PipeParameter(Code = true)]` and are read raw, never through `{{ }}` evaluation.
- **Sandbox:**
  - Every evaluation gets a fresh engine in strict mode without CLR access.
  - Data crosses the boundary only as JSON. Changes inside JavaScript never reach the items.
  - Limits are set in `PipeCoreOptions.Scripts`:
    - expressions: 1 s timeout, 100,000 statements
    - Code node: 30 s timeout, cancellable with the execution
    - both: 64 MB memory and 256 recursion depth
  - Parsed scripts are cached.
- **Results:** `undefined` becomes null, `Date` becomes ISO text, and functions and cyclic objects are errors.
  - Errors carry the expression, or the line number for Code nodes.

## Consequences

- **Behavior change:** `$json.missing.deeper` used to be null and is now an error, as in JavaScript. Use `$json.missing?.deeper`.
- **Performance:** each evaluation costs an engine instance plus a JSON round trip of the item. That is fine for typical workflows. With very many items and fields, reusing engines per node run would be the next optimization.
- **No async or network in scripts:** network access stays in nodes such as HTTP Request.
