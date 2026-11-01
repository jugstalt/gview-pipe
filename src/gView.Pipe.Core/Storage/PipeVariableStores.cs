using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Variables;
using gView.Pipe.Core.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace gView.Pipe.Core.Storage;

internal sealed class PipeMemoryVariableStore : IPipeVariableStore
{
    private List<PipeVariable> _variables = [];

    public Task<IReadOnlyList<PipeVariable>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PipeVariable>>(PipeJson.Clone(_variables));

    public Task SaveAsync(IReadOnlyList<PipeVariable> variables, CancellationToken cancellationToken = default)
    {
        _variables = PipeJson.Clone(variables.ToList());
        return Task.CompletedTask;
    }
}

/// <summary>All instance variables in <c>{root}/variables.json</c>; secret values are encrypted.</summary>
internal sealed class PipeFileVariableStore : IPipeVariableStore
{
    private readonly string _file;
    private readonly PipeSecretValues _secrets;

    public PipeFileVariableStore(IOptions<PipeFileStoreOptions> options, PipeSecretValues secrets)
    {
        _file = Path.Combine(Path.GetFullPath(options.Value.RootPath), "variables.json");
        _secrets = secrets;
    }

    public async Task<IReadOnlyList<PipeVariable>> ListAsync(CancellationToken cancellationToken = default)
    {
        var variables = File.Exists(_file)
            ? PipeJson.Deserialize<List<PipeVariable>>(await File.ReadAllTextAsync(_file, cancellationToken)) ?? []
            : [];
        _secrets.Unprotect(variables);
        return variables;
    }

    public Task SaveAsync(IReadOnlyList<PipeVariable> variables, CancellationToken cancellationToken = default)
        => PipeFiles.WriteAtomicAsync(_file, PipeJson.Serialize(_secrets.Protected(variables)), cancellationToken);
}

/// <summary>
/// Encrypts the values of secret variables for the file stores. A value that can't be decrypted (e.g. a workflow
/// copied from another installation) becomes empty.
/// </summary>
internal sealed class PipeSecretValues
{
    private const string Prefix = "protected:";
    private const string ProtectorPurpose = "gView.Pipe.Variables.v1";

    private readonly IDataProtector _protector;
    private readonly ILogger<PipeSecretValues> _logger;

    public PipeSecretValues(IDataProtectionProvider dataProtection, ILogger<PipeSecretValues> logger)
    {
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _logger = logger;
    }

    /// <summary>A copy with the secret values encrypted; the caller's objects stay unchanged.</summary>
    public List<PipeVariable>? Protected(IEnumerable<PipeVariable>? variables)
        => variables?.Select(v => new PipeVariable
        {
            Name = v.Name,
            Secret = v.Secret,
            Values = v.Values.ToDictionary(
                e => e.Key,
                e => v.Secret && e.Value.Length > 0 ? Prefix + _protector.Protect(e.Value) : e.Value,
                StringComparer.OrdinalIgnoreCase)
        }).ToList();

    /// <summary>Decrypts in place.</summary>
    public void Unprotect(IEnumerable<PipeVariable>? variables)
    {
        foreach (var variable in variables ?? [])
        {
            foreach (var (environment, value) in variable.Values.ToList())
            {
                if (!value.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    continue;
                }
                try
                {
                    variable.Values[environment] = _protector.Unprotect(value[Prefix.Length..]);
                }
                catch (CryptographicException ex)
                {
                    _logger.LogWarning(ex, "Can't decrypt the value of variable {Name} for {Environment}", variable.Name, environment);
                    variable.Values[environment] = "";
                }
            }
        }
    }
}
