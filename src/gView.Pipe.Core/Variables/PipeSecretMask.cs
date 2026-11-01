using gView.Pipe.Abstraction.Variables;
using System.Diagnostics.CodeAnalysis;

namespace gView.Pipe.Core.Variables;

static internal class PipeSecretMask
{
    public const string Mask = "***";

    /// <summary>Replaces the values of secret variables; values shorter than 3 characters are left, they'd mask too much.</summary>
    [return: NotNullIfNotNull(nameof(text))]
    static public string? Apply(string? text, PipeResolvedVariables variables)
    {
        if (text is null)
        {
            return null;
        }
        foreach (var secret in variables.SecretValues.Where(v => v.Length >= 3).OrderByDescending(v => v.Length))
        {
            text = text.Replace(secret, Mask, StringComparison.Ordinal);
        }
        return text;
    }
}
