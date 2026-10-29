using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Workflows;

namespace gView.Pipe.Core.Engine;

/// <summary>Validated, indexed view of a workflow. Only directed acyclic graphs are accepted.</summary>
internal sealed class PipeWorkflowGraph
{
    private PipeWorkflowGraph(
        IReadOnlyDictionary<string, PipeWorkflowNode> nodes,
        ILookup<string, PipeConnection> incoming,
        ILookup<string, PipeConnection> outgoing)
    {
        Nodes = nodes;
        Incoming = incoming;
        Outgoing = outgoing;
    }

    public IReadOnlyDictionary<string, PipeWorkflowNode> Nodes { get; }
    public ILookup<string, PipeConnection> Incoming { get; }
    public ILookup<string, PipeConnection> Outgoing { get; }

    public IEnumerable<string> Roots => Nodes.Keys.Where(id => !Incoming[id].Any());

    static public PipeWorkflowGraph Build(PipeWorkflow workflow)
    {
        var nodes = new Dictionary<string, PipeWorkflowNode>();
        var names = new HashSet<string>();

        foreach (var node in workflow.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                throw new PipeException($"Node '{node.Name}' has no id");
            }
            if (!nodes.TryAdd(node.Id, node))
            {
                throw new PipeException($"Duplicate node id '{node.Id}'");
            }
            if (!names.Add(node.Name))
            {
                throw new PipeException($"Duplicate node name '{node.Name}'");
            }
        }

        foreach (var connection in workflow.Connections)
        {
            if (!nodes.ContainsKey(connection.SourceNodeId) || !nodes.ContainsKey(connection.TargetNodeId))
            {
                throw new PipeException($"Connection {connection.SourceNodeId} -> {connection.TargetNodeId} references an unknown node");
            }
        }

        var graph = new PipeWorkflowGraph(
            nodes,
            workflow.Connections.ToLookup(c => c.TargetNodeId),
            workflow.Connections.ToLookup(c => c.SourceNodeId));

        graph.EnsureAcyclic();
        return graph;
    }

    public HashSet<string> ReachableFrom(IEnumerable<string> startNodeIds)
    {
        var reachable = new HashSet<string>();
        var stack = new Stack<string>(startNodeIds);

        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (reachable.Add(id))
            {
                foreach (var connection in Outgoing[id])
                {
                    stack.Push(connection.TargetNodeId);
                }
            }
        }

        return reachable;
    }

    private void EnsureAcyclic()
    {
        // Kahn: if a topological order can't consume all nodes, there is a cycle
        var inDegree = Nodes.Keys.ToDictionary(id => id, id => Incoming[id].Count());
        var queue = new Queue<string>(inDegree.Where(p => p.Value == 0).Select(p => p.Key));
        var visited = 0;

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            visited++;

            foreach (var connection in Outgoing[id])
            {
                if (--inDegree[connection.TargetNodeId] == 0)
                {
                    queue.Enqueue(connection.TargetNodeId);
                }
            }
        }

        if (visited != Nodes.Count)
        {
            var involved = inDegree.Where(p => p.Value > 0).Select(p => Nodes[p.Key].Name);
            throw new PipeException($"Workflow contains a cycle ({string.Join(", ", involved)}); loops are not supported");
        }
    }
}
