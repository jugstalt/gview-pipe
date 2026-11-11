using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Portal;
using gView.Pipe.Abstraction.Triggers;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Triggers;

public sealed class FormTriggerNodeParameters
{
    [PipeFormFields]
    [PipeParameter(Label = "Fields", Order = 1, Expressions = false,
        Description = "The inputs of the form. Each has a name (the property in the item), a label, a type (text, multiline text, number, boolean, date, choice or password), "
                      + "and optionally a description, a placeholder, a default, a regular expression the text must match as a whole (with the message shown otherwise) and the values of a choice.")]
    public List<JsonNode?> Fields { get; set; } = new();
}

[PipeNodeExamples]
[PipeNode(PipeBuiltInNodes.FormTrigger, "Form Trigger",
    Category = "Triggers",
    Description = "Starts the workflow from the user portal: declares the form the user fills in and outputs one item with the values. "
                  + "Without a Form Trigger a published workflow starts without input.")]
public sealed class FormTriggerNode : IPipeFormTrigger, IPipeNode<FormTriggerNodeParameters>
{
    public Task<IAsyncDisposable?> ActivateAsync(IPipeTriggerContext context, CancellationToken cancellationToken)
        => Task.FromResult<IAsyncDisposable?>(null);

    public PipeForm GetForm(JsonObject parameters)
        => PipeForm.FromJson(parameters["fields"] as JsonArray);

    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
        => Task.FromResult(PipeNodeResult.FromMain(context.GetInput()));
}
