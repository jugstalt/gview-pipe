using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Variables;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Options;
using Microsoft.Extensions.Options;

namespace gView.Pipe.Core.Variables;

internal sealed class PipeEnvironmentService : IPipeEnvironments
{
    private readonly PipeCoreOptions _options;
    private readonly IPipeWorkflowGroupStore _groupStore;
    private readonly IPipeVariableStore _variableStore;

    public PipeEnvironmentService(IOptions<PipeCoreOptions> options, IPipeWorkflowGroupStore groupStore, IPipeVariableStore variableStore)
    {
        _options = options.Value;
        _groupStore = groupStore;
        _variableStore = variableStore;

        Instance = string.IsNullOrWhiteSpace(_options.Environment) ? PipeEnvironments.Default : PipeEnvironments.Normalize(_options.Environment);
        // the instance's environment is always selectable, even if the list misses it (e.g. an older pipe.config)
        All = new[] { PipeEnvironments.Default }
            .Concat(_options.Environments.Where(e => !string.IsNullOrWhiteSpace(e)).Select(PipeEnvironments.Normalize))
            .Append(Instance)
            .Distinct()
            .ToList();
    }

    public IReadOnlyList<string> All { get; }

    public string Instance { get; }

    public PipeEffectiveEnvironment Resolve(PipeWorkflow workflow, PipeWorkflowGroup? group)
    {
        if (!string.IsNullOrWhiteSpace(workflow.Environment))
        {
            return new(PipeEnvironments.Normalize(workflow.Environment), PipeEnvironmentSource.Workflow);
        }
        if (group is not null && group.Id == workflow.GroupId && !string.IsNullOrWhiteSpace(group.Environment))
        {
            return new(PipeEnvironments.Normalize(group.Environment), PipeEnvironmentSource.Group, group.Name);
        }
        return new(Instance, PipeEnvironmentSource.Instance);
    }

    public async Task<PipeEffectiveEnvironment> ResolveAsync(PipeWorkflow workflow, CancellationToken cancellationToken = default)
        => Resolve(workflow, await GroupOfAsync(workflow, cancellationToken));

    public async Task<PipeResolvedVariables> ResolveVariablesAsync(PipeWorkflow workflow, string environment, CancellationToken cancellationToken = default)
    {
        var group = await GroupOfAsync(workflow, cancellationToken);
        // lowest level first
        IReadOnlyList<IReadOnlyList<PipeVariable>> levels =
        [
            workflow.Variables ?? [],
            group?.Variables ?? [],
            await GetInstanceVariablesAsync(cancellationToken)
        ];
        return Resolve(levels, PipeEnvironments.Normalize(environment));
    }

    /// <summary>The value for <paramref name="environment"/> on any level beats a DEFAULT value on any level.</summary>
    static internal PipeResolvedVariables Resolve(IReadOnlyList<IReadOnlyList<PipeVariable>> levels, string environment)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var secrets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = levels.SelectMany(l => l).Select(v => PipeVariableNames.Normalize(v.Name)).Where(n => PipeVariableNames.Validate(n) is null).Distinct();

        foreach (var name in names)
        {
            var definitions = levels.Select(l => l.FirstOrDefault(v => PipeVariableNames.Normalize(v.Name) == name)).OfType<PipeVariable>().ToList();
            var value = definitions.Select(d => ValueFor(d, environment)).FirstOrDefault(v => v is not null)
                        ?? definitions.Select(d => ValueFor(d, PipeEnvironments.Default)).FirstOrDefault(v => v is not null);
            if (value is null)
            {
                continue;
            }

            values[name] = value;
            if (definitions.Any(d => d.Secret))
            {
                secrets.Add(name);
            }
        }

        return new PipeResolvedVariables { Values = values, Secrets = secrets };
    }

    // stored dictionaries lose their comparer
    static private string? ValueFor(PipeVariable variable, string environment)
        => variable.Values.FirstOrDefault(v => PipeEnvironments.Normalize(v.Key) == environment) is { Key: not null } entry ? entry.Value : null;

    public async Task<IReadOnlyList<PipeVariable>> GetInstanceVariablesAsync(CancellationToken cancellationToken = default)
    {
        var variables = new Dictionary<string, PipeVariable>(StringComparer.OrdinalIgnoreCase);
        var configSecrets = _options.SecretVariables.Select(PipeVariableNames.Normalize).ToHashSet();

        foreach (var (name, values) in _options.Variables)
        {
            var normalized = PipeVariableNames.Normalize(name);
            variables[normalized] = new PipeVariable
            {
                Name = normalized,
                Secret = configSecrets.Contains(normalized),
                Values = values.ToDictionary(v => PipeEnvironments.Normalize(v.Key), v => v.Value, StringComparer.OrdinalIgnoreCase)
            };
        }

        // values of the store win per environment
        foreach (var stored in await _variableStore.ListAsync(cancellationToken))
        {
            var normalized = PipeVariableNames.Normalize(stored.Name);
            if (!variables.TryGetValue(normalized, out var variable))
            {
                variables[normalized] = variable = new PipeVariable { Name = normalized };
            }
            variable.Secret |= stored.Secret;
            foreach (var (environment, value) in stored.Values)
            {
                variable.Values[PipeEnvironments.Normalize(environment)] = value;
            }
        }

        return variables.Values.OrderBy(v => v.Name, StringComparer.Ordinal).ToList();
    }

    private async Task<PipeWorkflowGroup?> GroupOfAsync(PipeWorkflow workflow, CancellationToken cancellationToken)
        => string.IsNullOrEmpty(workflow.GroupId) ? null : await _groupStore.GetAsync(workflow.GroupId, cancellationToken);
}
