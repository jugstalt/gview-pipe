using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Portal;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Triggers;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Utility;

public enum ResultKind
{
    Text,
    Markdown,
    Table
}

public sealed class ResultNodeParameters
{
    [PipeParameter(Label = "Kind", Order = 1, Expressions = false,
        Description = "Text and Markdown show the content; Table shows the incoming items, one row per item")]
    public ResultKind Kind { get; set; } = ResultKind.Text;

    [PipeParameter(Label = "Title", Order = 2)]
    public string? Title { get; set; }

    [PipeParameter(Label = "Content", Order = 3, Multiline = true, Placeholder = "Done: {{ $json.count }} rows",
        Description = "For Text and Markdown; evaluated for the first incoming item")]
    public string? Content { get; set; }

    [PipeParameter(Label = "Error message", Order = 4,
        Description = "Shown to the portal user when the run fails. Without it the user sees a general message and the number of the run; details of errors stay with the admins")]
    public string? ErrorMessage { get; set; }
}

[PipeNode(PipeBuiltInNodes.Result, "Result",
    Category = "Utility",
    Description = "Defines what a user sees in the portal at the end of the run: text, Markdown or a table. Without a Result node the portal shows only the status. "
                  + "Outputs one item with the result, so place it at the end of the workflow.")]
public sealed class ResultNode : IPipeResultNode, IPipeNode<ResultNodeParameters>
{
    private const int MaxRows = 1000;

    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput();
        var parameters = context.GetParameters<ResultNodeParameters>(items.FirstOrDefault(), 0);
        var view = new PipeResultView
        {
            Kind = parameters.Kind.ToString().ToLowerInvariant(),
            Title = parameters.Title,
            ErrorMessage = parameters.ErrorMessage
        };

        if (parameters.Kind == ResultKind.Table)
        {
            var rows = items.Take(MaxRows).Select(i => i.Json).ToList();
            var columns = rows.SelectMany(r => r.Select(p => p.Key)).Distinct().ToList();
            view.Columns = columns;
            view.Rows = rows.Select(r => columns.Select(c => r[c] switch
            {
                null => null,
                JsonValue v when v.TryGetValue<string>(out var s) => s,
                var node => node.ToJsonString(PipeJson.Options)
            }).ToList()).ToList();
        }
        else
        {
            view.Text = parameters.Content;
        }

        return Task.FromResult(PipeNodeResult.FromMain([new PipeItem(PipeJson.ToJsonObject(view))]));
    }
}
