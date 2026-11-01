# Recipes

The sample workflows that come with gView.Pipe double as recipes. Each one solves a small task and teaches a few nodes. They are in the group **Samples** of an empty installation, and you can download the JSON files here.

!!! note "Using a downloaded file"
    There is no import button yet. Copy the file into the `workflows` folder inside the data folder of the app (see the [Admin Guide](../admin-guide/operations.md#backup)) and reload the workflow list; restart the app if the workflow does not appear. The file name is the id of the workflow. Workflows that call other workflows (`Execute Workflow`, error workflows) refer to them by id, so keep the ids.

## Hello Pipe

*Manual trigger → Set → If → Log.* [Download](../../downloads/workflows/hello-pipe.json)

The first workflow to open. **Set Greeting** adds fields to the item (`greeting` uses an expression), **If** (`{{ $json.score }}` greater than `10`) sends the item to `true` or `false`, and one of two **Log** nodes writes the result. Run it and look at the **Run** tab of each node. The branch that gets no items is skipped (gray).

## Code Transform

*Code → Switch → Set → Merge → Log.* [Download](../../downloads/workflows/code-transform.json)

A **Code** node generates four orders. **Switch** routes them by `{{ $json.country }}`: orders from AT go to one **Set** node (20 % VAT), orders from DE to another (19 %), and **Merge** joins the two branches again. Orders from other countries leave through the `fallback` output, which is not connected here, so they are dropped. A good model for "different handling per kind of item".

## Item Tools

*Split Out → Remove Duplicates → Sort → Limit → Aggregate.* [Download](../../downloads/workflows/item-tools.json)

Orders contain a list of order lines. **Split Out** turns each line into its own item, **Remove Duplicates** keeps one item per `sku`, **Sort** orders by price (descending), **Limit** keeps the first three, and **Aggregate** collects the SKUs into one item.

## Sub-workflow Demo and Price Calculator

*Execute Workflow per batch.* [Sub-workflow Demo](../../downloads/workflows/sub-workflow-demo.json) · [Price Calculator](../../downloads/workflows/price-calculator.json) · [Error Alerts](../../downloads/workflows/error-alerts.json)

**Price Calculator** starts with an **Execute Workflow Trigger** and adds VAT. **Sub-workflow Demo** calls it with **Execute Workflow** in mode *batches* (two items per call). That is how you build a loop: call a sub-workflow per item or per batch. The demo names **Error Alerts** as its error workflow: if the run fails, Error Alerts writes the failure to the log.

## API Demo

*Webhook → If → Respond to Webhook.* [Download](../../downloads/workflows/api-demo.json)

A small API: `POST /webhook/api/greet` with `{ "name": "Ada" }` answers `200` with a greeting, and without a name `400` with an error text. The trigger waits for a **Respond to Webhook** node (*Using respond node*). Activate the workflow first.

```bash
curl -i -X POST http://localhost:5240/webhook/api/greet -H "Content-Type: application/json" -d '{"name":"Ada"}'
```

See [Triggers and webhooks](triggers.md#respond-to-webhook).

## Webhook Demo

*Webhook → Set.* [Download](../../downloads/workflows/webhook-demo.json)

The shortest webhook: `POST /webhook/demo` answers with a greeting. The trigger is set to *When finished*, so the caller receives the items of the last node. The workflow is inactive until you switch it on.

## File Export

*Code → Write File → Read File.* [Download](../../downloads/workflows/file-export.json)

Writes orders as a CSV file into the released folder `files` (`exports/orders-<date>.csv`) and reads the file back as items. The folder name `files` has to be released by the administrator (`Pipe:FileRoots`), see the [Admin Guide](../admin-guide/configuration.md).

## Build your own

- **Call an API on a schedule:** Cron Trigger → HTTP Request → Code or Set → Send Email.
- **React to a webhook and answer:** Webhook Trigger (*Using respond node*) → your nodes → Respond to Webhook, with an error output for failures ([Error handling](error-handling.md#answering-a-webhook-after-a-failure)).
- **Run a script nightly and get a mail on failure:** Cron Trigger → Run Script, with an error workflow that sends the mail.
- **Copy rows between databases:** Cron Trigger → SQL (select) → SQL (insert, once per item).
