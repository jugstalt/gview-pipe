using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Nodes;

namespace gView.Pipe.Plugins.AI;

public enum AzureOpenAIAuthentication
{
    /// <summary>The key of the Azure OpenAI resource, sent as api-key header.</summary>
    ApiKey,

    /// <summary>A token of DefaultAzureCredential (managed identity in Azure, the developer login locally).</summary>
    ManagedIdentity
}

[PipeCredentialType(AzureOpenAICredential.TypeId, "Azure OpenAI",
    Description = "A model deployment of Azure OpenAI for the AI node")]
public sealed class AzureOpenAICredential
{
    public const string TypeId = "848d5271-c4bc-4a38-bc50-cfc6538c01d4";

    public const string DefaultApiVersion = "2025-01-01-preview";

    [PipeParameter(Label = "Authentication", Order = 1)]
    public AzureOpenAIAuthentication Authentication { get; set; } = AzureOpenAIAuthentication.ApiKey;

    [PipeParameter(Label = "Endpoint", Order = 2, Placeholder = "https://<resource>.openai.azure.com",
        Description = "The endpoint of the resource, or the whole URL of the deployment as the Azure portal shows it; then deployment and API version come from it")]
    public string Endpoint { get; set; } = "";

    [PipeParameter(Label = "Deployment", Order = 3, Description = "The name of the model deployment")]
    public string? Deployment { get; set; }

    [PipeParameter(Label = "API version", Order = 4, Placeholder = DefaultApiVersion)]
    public string? ApiVersion { get; set; }

    [Secret, PipeParameter(Label = "API key", Order = 5, Description = "API key: one of the keys of the resource")]
    public string? ApiKey { get; set; }
}
