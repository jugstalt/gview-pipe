using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Secrets;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Secrets;

static internal class PipeCredentialSecrets
{
    /// <summary>
    /// Replaces the fields of the credential that stand for a secret of an external source (ADR 0033) by its value.
    /// Used by nodes and by triggers, so both see the same credential.
    /// </summary>
    /// <returns>The values that were read, so the caller can mask them.</returns>
    static public async Task<IReadOnlyList<string>> ResolveAsync(PipeCredential credential, IPipeSecretResolver resolver, CancellationToken cancellationToken)
    {
        var resolved = new List<string>();
        foreach (var (field, node) in credential.Data.ToList())
        {
            if (node is JsonValue json && json.TryGetValue<string>(out var text) && PipeSecretReference.TryParse(text, out var reference))
            {
                try
                {
                    var value = await resolver.ResolveAsync(reference, cancellationToken);
                    credential.Data[field] = value;
                    resolved.Add(value);
                }
                catch (PipeException ex)
                {
                    throw new PipeException($"Credential '{credential.Name}', field {field}: {ex.Message}");
                }
            }
        }
        return resolved;
    }
}
