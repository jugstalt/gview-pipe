using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Expressions;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace gView.Pipe.Abstraction.Nodes;

public interface IPipeNodeContext
{
    string WorkflowId { get; }
    string WorkflowName { get; }
    string ExecutionId { get; }
    string NodeId { get; }
    string NodeName { get; }

    /// <summary>Ports that received data (connected input ports).</summary>
    IReadOnlyCollection<string> InputPorts { get; }

    IReadOnlyList<PipeItem> GetInput(string port = PipePorts.Main);

    /// <summary>The node's parameters as stored in the workflow, expressions not evaluated.</summary>
    JsonObject RawParameters { get; }

    /// <summary>
    /// Returns the parameters with all <c>{{ }}</c> expressions evaluated against <paramref name="item"/>.
    /// </summary>
    TParameters GetParameters<TParameters>(PipeItem? item = null, int itemIndex = 0)
        where TParameters : class, new();

    /// <summary>Evaluates a template string such as <c>"Hello {{ $json.name }}"</c>.</summary>
    JsonNode? Evaluate(string template, PipeItem? item = null, int itemIndex = 0);

    /// <summary>The variables expressions see for <paramref name="item"/>; pass it to <see cref="IPipeScriptRunner"/>.</summary>
    PipeExpressionScope CreateExpressionScope(PipeItem? item = null, int itemIndex = 0);

    Task<PipeCredential> GetCredentialAsync(string credentialId, CancellationToken cancellationToken = default);

    Task<TData> GetCredentialAsync<TData>(string credentialId, CancellationToken cancellationToken = default)
        where TData : class, new();

    IPipeBinaryStore BinaryStore { get; }

    /// <summary>
    /// Runs another workflow as a sub-workflow, starting at its <see cref="Triggers.IPipeSubWorkflowTrigger"/> node
    /// with <paramref name="items"/>, and returns its execution once it has finished (successful or not).
    /// The sub-workflow shares this execution's binary store.
    /// </summary>
    /// <exception cref="PipeException">The workflow doesn't exist, has no such trigger, is already running too often,
    /// or the nesting limit is reached.</exception>
    Task<PipeExecution> ExecuteWorkflowAsync(string workflowId, IReadOnlyList<PipeItem> items, CancellationToken cancellationToken = default);

    ILogger Logger { get; }

    /// <summary>Services of the current execution scope.</summary>
    IServiceProvider Services { get; }
}
