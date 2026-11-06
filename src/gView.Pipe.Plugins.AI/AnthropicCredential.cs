using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Nodes;
using System.ComponentModel.DataAnnotations;

namespace gView.Pipe.Plugins.AI;

[PipeCredentialType(AnthropicCredential.TypeId, "Anthropic API",
    Description = "API key of the Anthropic API for the AI node (Claude)")]
public sealed class AnthropicCredential
{
    public const string TypeId = "f906caca-2310-49a7-b9f2-d7b4cc23575a";

    [Required, Secret, PipeParameter(Label = "API key", Order = 1, Description = "From the Claude Console; the account of the key pays for the calls")]
    public string ApiKey { get; set; } = "";
}
