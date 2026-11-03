using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Secrets;

namespace gView.Pipe.Core.Secrets;

/// <summary>
/// Environment variables with a required prefix: the secret <c>db-password</c> reads <c>PIPE_SECRET_DB_PASSWORD</c>.
/// The prefix keeps workflows away from the other variables of the host (ADR 0033).
/// </summary>
internal sealed class PipeEnvironmentSecretProvider : IPipeSecretProvider, IPipeSecretLookupHint
{
    public const string SourceName = "env";
    public const string DefaultPrefix = "PIPE_SECRET_";

    private readonly string _prefix;
    private readonly Func<string, string?> _read;

    public PipeEnvironmentSecretProvider(string prefix, Func<string, string?>? read = null)
    {
        _prefix = string.IsNullOrWhiteSpace(prefix) ? DefaultPrefix : prefix;
        _read = read ?? Environment.GetEnvironmentVariable;
    }

    public Task<string?> GetAsync(string name, CancellationToken cancellationToken = default)
        => Task.FromResult(_read(VariableOf(name)));

    public string Describe(string name) => $"looked for the environment variable {VariableOf(name)}";

    private string VariableOf(string name)
    {
        if (name.Contains('@'))
        {
            throw new PipeException("The source 'env' has no versions; use the name without '@'");
        }
        if (!name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_'))
        {
            throw new PipeException($"Invalid secret name '{name}': use letters, digits, '-', '.' and '_'");
        }

        return _prefix + name.ToUpperInvariant().Replace('-', '_').Replace('.', '_');
    }
}
