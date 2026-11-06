using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Nodes;

namespace gView.Pipe.Plugins.AzureStorage;

public enum AzureStorageAuthentication
{
    /// <summary>The connection string of the account, also for the emulator Azurite.</summary>
    ConnectionString,

    /// <summary>The account URL; signed in with DefaultAzureCredential (managed identity in Azure, the developer login locally).</summary>
    ManagedIdentity,

    /// <summary>The account URL and a shared access signature.</summary>
    SasToken
}

[PipeCredentialType(AzureStorageCredential.TypeId, "Azure Storage",
    Description = "Storage account for the Azure Blob node and trigger: connection string, managed identity or SAS token")]
public sealed class AzureStorageCredential
{
    public const string TypeId = "8d237e62-b6d3-4f25-8450-0171baefa0b7";

    [PipeParameter(Label = "Authentication", Order = 1)]
    public AzureStorageAuthentication Authentication { get; set; } = AzureStorageAuthentication.ConnectionString;

    [Secret, PipeParameter(Label = "Connection string", Order = 2, Description = "Connection string: from the access keys of the account")]
    public string? ConnectionString { get; set; }

    [PipeParameter(Label = "Account URL", Order = 3, Placeholder = "https://<account>.blob.core.windows.net",
        Description = "Managed identity and SAS token: the blob endpoint of the account")]
    public string? AccountUrl { get; set; }

    [Secret, PipeParameter(Label = "SAS token", Order = 4, Description = "SAS token: without the leading ?")]
    public string? SasToken { get; set; }
}
