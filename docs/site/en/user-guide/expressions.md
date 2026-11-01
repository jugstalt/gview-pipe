# Items and expressions

## Items

Data flows through a workflow as **items**. An item is a JSON object, plus optional binary attachments (files). A connection carries a list of items, and a node receives all items of its inputs at once.

```json
{ "id": 1, "customer": "Ada", "total": 120, "country": "AT" }
```

Most nodes process each item on its own: the Set node adds fields to every item, If sends each item to `true` or `false`. A few nodes work on the whole list: Aggregate collects many items into one, Split Out turns one item into many, Limit and Sort change the list.

Items are copied on their way into a node, so parallel branches can't change each other's data.

## Expressions

Any text parameter can contain **expressions** in double curly braces. The content is JavaScript:

```
Hello {{ $json.customer }}, your total is {{ $json.total * 1.2 }} EUR
```

- A parameter that consists of **one** expression keeps the type of the result: `{{ $json.total * 1.2 }}` is a number, `{{ $json.tags }}` an array.
- With text around the expressions, the result is **text**.
- Expressions run once per item, with that item as `$json`.

### Variables

| Variable | Meaning |
|---|---|
| `$json` | The JSON of the current item. |
| `$itemIndex` | The position of the current item in the list (from 0). |
| `$input.first()`, `$input.all()` | The first item or all items of the node's input. |
| `$node["Name"].json` | The first item that the node *Name* produced. `.all` is the list of all its items. |
| `$workflow` | Id and name of the workflow. |
| `$execution` | Id of the current run. |
| `$now` | The current time as ISO text. |

There is no `$env`: environment variables often contain secrets. For secrets, use [credentials](credentials.md).

### Examples

| Goal | Expression |
|---|---|
| A nested field | `{{ $json.address.city }}` |
| Safe access when the field may be missing | `{{ $json.address?.city ?? 'unknown' }}` |
| Calculate | `{{ Math.round($json.total * 1.2 * 100) / 100 }}` |
| Join text | `{{ $json.first + ' ' + $json.last }}` |
| Condition | `{{ $json.total > 100 ? 'big' : 'small' }}` |
| Data of an earlier node | `{{ $node["Products"].json.name }}` |
| Webhook body | `{{ $json.body?.name ?? 'stranger' }}` |
| Count the items | `{{ $input.all().length }}` |

!!! warning
    Accessing a field of something that doesn't exist is an **error**, as in JavaScript: `$json.missing.deeper` fails if `missing` isn't there. Use `?.`: `$json.missing?.deeper`.

Values are copied into the expression: changing `$json` inside an expression doesn't change the item. Expressions can't use the network or files and are stopped after one second.

### The editor helps

In the **Run** tab of a node, click a field of the input to put the matching expression into the parameter you edited last. That is the quickest way to get the path right.

## The Code node

For anything larger than an expression, the **Code** node runs JavaScript. It has two modes:

- **All items:** your code gets `items` (an array with the JSON of all input items) and returns an array of objects, which become the output items.
- **Each item:** your code runs once per item with `$json` and returns an object, or `null` to drop the item.

```javascript
// mode: all items
const orders = items.filter(order => order.total > 100);
console.log(`${orders.length} big orders`);
return orders.map(order => ({ ...order, big: true }));
```

```javascript
// mode: each item
if ($json.country === 'DE') return null;
return { ...$json, vat: $json.total * 0.2 };
```

- `console.log` writes to the **Logs** of the node.
- The variables of expressions (`$node`, `$workflow`, `$now`, …) are available.
- The code is not scanned for `{{ }}`; write plain JavaScript.
- A Code node may run up to 30 seconds. It can't use the network or files: use the HTTP Request or Read File nodes for that.
- Attachments of an item are kept when the number of items stays the same.

## Attachments

Files (for example from a webhook upload or the Read File node) travel with the item as **binary attachments** by name. They are not part of the JSON. Nodes such as Write File and Send Email can use them. The bytes of attachments exist only while the execution runs; in the history you see that an attachment existed, but can't download it afterwards.
