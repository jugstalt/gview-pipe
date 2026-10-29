using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Plugins;

internal sealed class PipeNodeRegistry : IPipeNodeRegistry
{
    private readonly Dictionary<Guid, PipeNodeDescriptor> _nodes;
    private readonly IServiceProvider _services;

    public PipeNodeRegistry(PipePluginCatalog catalog, IServiceProvider services, ILogger<PipeNodeRegistry> logger)
    {
        _nodes = catalog.Nodes.ToDictionary(n => n.Id);
        _services = services;

        foreach (var warning in catalog.Warnings)
        {
            logger.LogWarning("Plugins: {Warning}", warning);
        }

        logger.LogInformation("Plugins: {NodeCount} node types from {AssemblyCount} assemblies",
            _nodes.Count, catalog.Assemblies.Count);
    }

    public IReadOnlyCollection<PipeNodeDescriptor> Nodes => _nodes.Values;

    public PipeNodeDescriptor? Find(Guid id) => _nodes.GetValueOrDefault(id);

    public IReadOnlyList<PipePort> GetInputs(PipeNodeDescriptor descriptor, JsonObject parameters)
        => descriptor.HasDynamicPorts
            ? CreatePortProvider(descriptor).GetInputs(parameters)
            : descriptor.Inputs;

    public IReadOnlyList<PipePort> GetOutputs(PipeNodeDescriptor descriptor, JsonObject parameters, PipeNodeSettings? settings = null)
    {
        var outputs = descriptor.HasDynamicPorts
            ? CreatePortProvider(descriptor).GetOutputs(parameters)
            : descriptor.Outputs;

        if (settings?.OnError == PipeNodeErrorMode.ErrorOutput && outputs.All(p => p.Name != PipePorts.Error))
        {
            outputs = [.. outputs, new PipePort(PipePorts.Error, "Error")];
        }

        return outputs;
    }

    private IPipeDynamicPorts CreatePortProvider(PipeNodeDescriptor descriptor)
        => (IPipeDynamicPorts)ActivatorUtilities.CreateInstance(_services, descriptor.NodeType);
}

internal sealed class PipeCredentialTypeRegistry : IPipeCredentialTypeRegistry
{
    private readonly Dictionary<Guid, PipeCredentialTypeDescriptor> _types;

    public PipeCredentialTypeRegistry(PipePluginCatalog catalog)
    {
        _types = catalog.CredentialTypes.ToDictionary(t => t.Id);
    }

    public IReadOnlyCollection<PipeCredentialTypeDescriptor> CredentialTypes => _types.Values;

    public PipeCredentialTypeDescriptor? Find(Guid id) => _types.GetValueOrDefault(id);
}
