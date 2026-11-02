using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Engine;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;

namespace gView.Pipe.Core.Workflows;

/// <summary>
/// The graph (unique ids and names, known connections, no cycles), installed node types and the parameters
/// (DataAnnotations) are errors; credentials that don't exist are warnings.
/// </summary>
internal sealed class PipeWorkflowValidator : IPipeWorkflowValidator
{
    private readonly IPipeNodeRegistry _registry;
    private readonly IPipeCredentialStore _credentials;

    public PipeWorkflowValidator(IPipeNodeRegistry registry, IPipeCredentialStore credentials)
    {
        _registry = registry;
        _credentials = credentials;
    }

    public async Task<PipeWorkflowValidation> ValidateAsync(PipeWorkflow workflow, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(workflow.Name))
        {
            errors.Add("The workflow has no name");
        }
        try
        {
            PipeWorkflowGraph.Build(workflow);
        }
        catch (PipeException ex)
        {
            errors.Add(ex.Message);
        }

        var credentialIds = (await _credentials.ListAsync(cancellationToken)).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var node in workflow.Nodes)
        {
            if (_registry.Find(node.Type) is not { } descriptor)
            {
                errors.Add($"Node '{node.Name}': unknown node type {node.Type}. Is the plugin installed?");
                continue;
            }
            // older parameters are migrated before they run; they can't be checked against the current class
            if (descriptor.ParametersType is not { } type || node.TypeVersion < descriptor.Version)
            {
                continue;
            }

            object? parameters;
            try
            {
                parameters = node.Parameters.Deserialize(type, PipeJson.Options);
            }
            catch (JsonException ex)
            {
                errors.Add($"Node '{node.Name}': invalid parameters: {ex.Message}");
                continue;
            }
            if (parameters is null)
            {
                continue;
            }

            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(parameters, new ValidationContext(parameters), results, validateAllProperties: true))
            {
                errors.AddRange(results.Select(r => $"Node '{node.Name}': {r.ErrorMessage}"));
            }

            foreach (var property in type.GetProperties().Where(p => p.GetCustomAttribute<PipeCredentialAttribute>() is not null))
            {
                if (property.GetValue(parameters) is string { Length: > 0 } id && !id.Contains("{{", StringComparison.Ordinal) && !credentialIds.Contains(id))
                {
                    warnings.Add($"Node '{node.Name}': credential '{id}' doesn't exist here");
                }
            }
        }

        return new PipeWorkflowValidation(errors, warnings);
    }
}
