using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Flow;

public enum MergeMode
{
    /// <summary>Items of input 1 followed by the items of input 2.</summary>
    Append,

    /// <summary>Merges the JSON of item n of both inputs into one item (input 2 wins on conflicts).</summary>
    CombineByIndex
}

public sealed class MergeNodeParameters
{
    [PipeParameter(Label = "Mode", Expressions = false)]
    public MergeMode Mode { get; set; } = MergeMode.Append;
}

[PipeNode(PipeBuiltInNodes.Merge, "Merge",
    Category = "Flow",
    Description = "Waits for both inputs and merges their items.",
    Inputs = new[] { Input1, Input2 })]
public sealed class MergeNode : IPipeNode<MergeNodeParameters>
{
    public const string Input1 = "input1";
    public const string Input2 = "input2";

    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var parameters = context.GetParameters<MergeNodeParameters>();
        var first = context.GetInput(Input1);
        var second = context.GetInput(Input2);

        var result = parameters.Mode switch
        {
            MergeMode.CombineByIndex => CombineByIndex(first, second),
            _ => first.Concat(second).ToList()
        };

        return Task.FromResult(PipeNodeResult.FromMain(result));
    }

    static private List<PipeItem> CombineByIndex(IReadOnlyList<PipeItem> first, IReadOnlyList<PipeItem> second)
    {
        var result = new List<PipeItem>();
        for (var i = 0; i < Math.Max(first.Count, second.Count); i++)
        {
            var json = i < first.Count ? (JsonObject)first[i].Json.DeepClone() : new JsonObject();
            var binary = i < first.Count ? new Dictionary<string, PipeBinaryRef>(first[i].Binary) : new();

            if (i < second.Count)
            {
                foreach (var (key, value) in second[i].Json)
                {
                    json[key] = value?.DeepClone();
                }
                foreach (var (key, value) in second[i].Binary)
                {
                    binary[key] = value;
                }
            }

            result.Add(new PipeItem(json) { Binary = binary });
        }
        return result;
    }
}
