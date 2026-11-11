# Parameters and the editor form

The editor builds the form of a node from its parameter class. It shows every public property with a getter **and** setter, in order. Attributes only refine how a property looks. The same class is filled from the stored JSON when the node runs.

```csharp
public enum ThingMethod
{
    [PipeEnumValue("Create a new thing")]
    Post,

    [PipeEnumValue("Replace an existing thing")]
    Put
}

public sealed class SendThingParameters
{
    [Required, PipeParameter(Label = "Url", Order = 1, Placeholder = "https://example.com/api",
        Description = "Address of the API, e.g. https://example.com/api/things")]
    public string Url { get; set; } = "";

    [PipeParameter(Label = "Method", Order = 2, Expressions = false, Description = "What the call does")]
    public ThingMethod Method { get; set; } = ThingMethod.Post;

    [PipeParameter(Label = "Id", Order = 3, ShowWhen = nameof(Method), ShowWhenValues = ["put"],
        Description = "The thing to replace, e.g. {{ $json.id }}")]
    public string? Id { get; set; }

    [PipeParameter(Label = "Headers", Order = 4, Advanced = true, Description = "Request headers, e.g. Accept → application/json")]
    public Dictionary<string, string> Headers { get; set; } = new();

    [PipeCredential(PipeBuiltInCredentials.BearerToken)]
    [PipeParameter(Label = "Credential", Expressions = false, Description = "The token the API expects")]
    public string? CredentialId { get; set; }
}
```

## `[PipeParameter]`

| Property | Effect |
|---|---|
| `Label` | The field label. Without it the property name is split into words (`StopOnFirstFailure` → "Stop on first failure"). |
| `Description` | Helper text under the field, and the description in the generated node reference. |
| `Placeholder` | Example text in an empty field. |
| `Order` | Position in the form. Properties without an order follow in declaration order. |
| `Multiline` | A larger text field. |
| `Expressions` | Whether the field offers `{{ }}` expressions (default `true` for text and JSON). Switch it off for fixed values such as modes and paths of configuration. |
| `Code` | Source code: a large monospace field, never evaluated as an expression. Read such values from `context.RawParameters`. |
| `ShowWhen`, `ShowWhenValues` | The field shows only while another property (`nameof(…)`) has one of the values: enum names, option values or `true`/`false`, without case. A hidden field keeps its value. One property only; a node that needs more conditions does too much. |
| `Advanced` | The field is in the folded *Advanced* section at the end of the form. Use it for settings most people never change: attachment names, timeouts, encodings. |

Every property, every enum value and every node needs a `Description`; a test of the repository checks all plugins in it. Write one line: what the field does, with an example, in the words of the people who use the node. Don't repeat the label and don't use internal words.

## `[PipeEnumValue]`

On the members of an enum used as a parameter: `[PipeEnumValue("What this choice does", Label = "Shown name")]`. The description is the second line of the entry in the dropdown and appears below the field for the chosen value; the label replaces the split member name (`ItemsAsCsv` → "All items as CSV").

## Types and fields

| C# type | Field |
|---|---|
| `string` | Text field. |
| `bool` | Switch. |
| `int`, `long`, `double`, `decimal` | Number field. |
| `enum` | Selection of the values, stored as camelCase text (`WhenFinished` → `whenFinished`). |
| `JsonNode?` | A field that accepts text, numbers or JSON, with expressions. |
| `Dictionary<string, string>` | A list of name/value pairs. |
| `List<string>` | A list of values. |
| `List<PipeSelection>` with `[PipeOptions]` | An ordered list of options that can be switched on and off. |

`[Required]` (DataAnnotations) marks a field as required in the form and in the node reference.

## Other attributes

| Attribute | Use |
|---|---|
| `[PipeCredential(typeIds…)]` on a `string?` | The field is a drop-down with the stored credentials of those types. It holds the credential's id. |
| `[PipeWorkflowReference]` on a `string?` | A workflow picker. It holds the workflow's id. |
| `[PipeOptions]` | Values computed by the server (`IPipeParameterOptions`), each with an optional label and description. A required field with exactly one value is filled in. |
| `[Secret]` (credential classes) | The field is masked and never shown again. |

## Names in JSON

Parameters are stored as camelCase JSON (`CredentialId` → `credentialId`) with enums as camelCase strings. Always use `PipeJson.Options` from `gView.Pipe.Abstraction.Serialization` when you serialize or deserialize parameters yourself, so you bind exactly like the engine and the editor.

## Node reference

The user documentation lists every node with its parameters, **generated from these attributes**: `dotnet run --project src/gView.Pipe.Docs`. Write `Description` texts for the people who read that page: what the field does, its unit and what happens when it is empty.
