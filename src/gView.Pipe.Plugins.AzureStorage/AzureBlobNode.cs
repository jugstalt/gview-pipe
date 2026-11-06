using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace gView.Pipe.Plugins.AzureStorage;

public enum AzureBlobOperation
{
    List,
    Download,
    Upload,
    Delete,
    Rename
}

public sealed class AzureBlobNodeParameters
{
    [Required, PipeCredential(AzureStorageCredential.TypeId)]
    [PipeParameter(Label = "Storage account", Order = 1, Expressions = false, Description = "Azure Storage credential")]
    public string? CredentialId { get; set; }

    [PipeParameter(Label = "Operation", Order = 2, Expressions = false)]
    public AzureBlobOperation Operation { get; set; } = AzureBlobOperation.List;

    [Required, PipeParameter(Label = "Container", Order = 3, Placeholder = "incoming")]
    public string Container { get; set; } = "";

    [PipeParameter(Label = "Path", Order = 4, Placeholder = "orders/2026/orders.csv",
        Description = "Name of the blob; folders are parts before a /. List: the folder, empty for the whole container")]
    public string Path { get; set; } = "";

    [PipeParameter(Label = "New container", Order = 5, Description = "Rename: the target container; empty for the same")]
    public string NewContainer { get; set; } = "";

    [PipeParameter(Label = "New path", Order = 6, Description = "Rename: where the blob goes; also to move it")]
    public string NewPath { get; set; } = "";

    [PipeParameter(Label = "File pattern", Order = 7, Expressions = false, Placeholder = "*.csv",
        Description = "List: * and ? on the name of the blob; capitals don't matter")]
    public string Pattern { get; set; } = "*";

    [PipeParameter(Label = "Include subfolders", Order = 8, Expressions = false, Description = "List")]
    public bool IncludeSubfolders { get; set; }

    [PipeParameter(Label = "Overwrite", Order = 9, Expressions = false, Description = "Upload and Rename: replace a blob that exists. Otherwise that is an error")]
    public bool Overwrite { get; set; }

    [PipeParameter(Label = "Create container", Order = 10, Expressions = false, Description = "Upload and Rename: create the container if it doesn't exist")]
    public bool CreateContainer { get; set; }

    [PipeParameter(Label = "Binary property", Order = 11, Expressions = false, Description = "Download: where the content goes. Upload: where it comes from")]
    public string BinaryProperty { get; set; } = "data";

    [PipeParameter(Label = "Max file size (MB)", Order = 12, Expressions = false, Description = "Download: larger blobs are refused before they are loaded")]
    [Range(1, 10240)]
    public int MaxFileSizeMb { get; set; } = 100;

    [PipeParameter(Label = "Timeout (seconds)", Order = 13, Expressions = false, Description = "For each request to Azure")]
    [Range(1, 3600)]
    public int TimeoutSeconds { get; set; } = 30;
}

[PipeNode(AzureBlobNode.NodeId, "Azure Blob",
    Category = "Files",
    Description = "Lists, downloads, uploads, deletes and renames blobs in Azure Blob Storage. Runs once per input item. "
                  + "Content comes and goes as binary attachments of the items; Read File and Write File connect it to local folders.")]
public sealed class AzureBlobNode : IPipeNode<AzureBlobNodeParameters>
{
    public const string NodeId = "eff26ad3-e0ba-4a53-9e43-c9fbfa5066b2";

    /// <summary>More blobs than this in a List are an error, so nothing is missing silently.</summary>
    public const int MaxListEntries = 10000;

    private readonly IPipeBlobConnector _connector;

    public AzureBlobNode(IPipeBlobConnector connector)
    {
        _connector = connector;
    }

    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput();
        if (items.Count == 0)
        {
            return PipeNodeResult.FromMain([]);
        }

        // credential and timeout are the same for all items
        var first = context.GetParameters<AzureBlobNodeParameters>(items[0], 0);
        if (string.IsNullOrWhiteSpace(first.CredentialId))
        {
            throw new PipeException("Select the Azure Storage credential");
        }
        var credential = await context.GetCredentialAsync<AzureStorageCredential>(first.CredentialId, cancellationToken);
        var session = _connector.Connect(credential, TimeSpan.FromSeconds(Math.Clamp(first.TimeoutSeconds, 1, 3600)));

        var output = new List<PipeItem>();
        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parameters = context.GetParameters<AzureBlobNodeParameters>(items[i], i);
            var container = BlobPaths.Container(parameters.Container, "Container");
            context.Logger.LogDebug("Azure Blob {Operation} {Container}/{Path}", parameters.Operation, container, parameters.Path);

            switch (parameters.Operation)
            {
                case AzureBlobOperation.List:
                    output.AddRange(await ListAsync(session, container, parameters, cancellationToken));
                    break;
                case AzureBlobOperation.Download:
                    output.Add(await DownloadAsync(session, context, items[i], container, parameters, cancellationToken));
                    break;
                case AzureBlobOperation.Upload:
                    output.Add(await UploadAsync(session, context, items[i], container, parameters, cancellationToken));
                    break;
                case AzureBlobOperation.Delete:
                    output.Add(await DeleteAsync(session, items[i], container, parameters, cancellationToken));
                    break;
                default:
                    output.Add(await RenameAsync(session, items[i], container, parameters, cancellationToken));
                    break;
            }
        }

        return PipeNodeResult.FromMain(output);
    }

    static private async Task<List<PipeItem>> ListAsync(IPipeBlobSession session, string container, AzureBlobNodeParameters parameters, CancellationToken cancellationToken)
    {
        var blobs = await session.ListAsync(container, BlobPaths.Prefix(parameters.Path), parameters.IncludeSubfolders, MaxListEntries, cancellationToken);
        var pattern = BlobPaths.PatternToRegex(parameters.Pattern);
        return blobs
            .Where(b => pattern.IsMatch(b.Name))
            .OrderBy(b => b.Path, StringComparer.Ordinal)
            .Select(b => new PipeItem(BlobPaths.Describe(b)))
            .ToList();
    }

    static private async Task<PipeItem> DownloadAsync(IPipeBlobSession session, IPipeNodeContext context, PipeItem input, string container, AzureBlobNodeParameters parameters, CancellationToken cancellationToken)
    {
        var path = BlobPaths.Required(parameters.Path, "Path");
        var blob = await session.StatAsync(container, path, cancellationToken) ?? throw new PipeException($"'{container}/{path}' not found");
        var limit = (long)Math.Clamp(parameters.MaxFileSizeMb, 1, 10240) * 1024 * 1024;
        if (blob.Size > limit)
        {
            throw new PipeException($"'{container}/{path}' is {blob.Size / 1024 / 1024} MB, more than the allowed {parameters.MaxFileSizeMb} MB");
        }

        // a temp file that goes away with the stream, so large blobs don't fill the memory
        await using var temp = new FileStream(System.IO.Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        await session.DownloadAsync(container, path, temp, cancellationToken);
        temp.Position = 0;
        var reference = await context.BinaryStore.StoreAsync(temp, blob.Name, blob.ContentType ?? "application/octet-stream", cancellationToken);

        var item = input.Clone();
        BlobPaths.Fill(item.Json, blob);
        item.Binary[BinaryName(parameters)] = reference;
        return item;
    }

    static private async Task<PipeItem> UploadAsync(IPipeBlobSession session, IPipeNodeContext context, PipeItem input, string container, AzureBlobNodeParameters parameters, CancellationToken cancellationToken)
    {
        var path = BlobPaths.Required(parameters.Path, "Path");
        var property = BinaryName(parameters);
        if (!input.Binary.TryGetValue(property, out var reference))
        {
            throw new PipeException($"The item has no attachment '{property}' to upload");
        }
        await EnsureContainerAsync(session, container, parameters.CreateContainer, cancellationToken);

        await using (var stream = await context.BinaryStore.OpenReadAsync(reference, cancellationToken))
        {
            await session.UploadAsync(container, path, stream, reference.MimeType, parameters.Overwrite, cancellationToken);
        }

        var item = input.Clone();
        item.Json["container"] = container;
        item.Json["path"] = path;
        item.Json["fileName"] = BlobPaths.Name(path);
        item.Json["size"] = reference.Length;
        return item;
    }

    static private async Task<PipeItem> DeleteAsync(IPipeBlobSession session, PipeItem input, string container, AzureBlobNodeParameters parameters, CancellationToken cancellationToken)
    {
        var path = BlobPaths.Required(parameters.Path, "Path");
        if (await session.StatAsync(container, path, cancellationToken) is null)
        {
            throw new PipeException($"'{container}/{path}' not found");
        }

        await session.DeleteAsync(container, path, cancellationToken);
        var item = input.Clone();
        item.Json["container"] = container;
        item.Json["path"] = path;
        item.Json["deleted"] = true;
        return item;
    }

    static private async Task<PipeItem> RenameAsync(IPipeBlobSession session, PipeItem input, string container, AzureBlobNodeParameters parameters, CancellationToken cancellationToken)
    {
        var path = BlobPaths.Required(parameters.Path, "Path");
        var newPath = BlobPaths.Required(parameters.NewPath, "New path");
        var newContainer = string.IsNullOrWhiteSpace(parameters.NewContainer) ? container : BlobPaths.Container(parameters.NewContainer, "New container");
        if (await session.StatAsync(container, path, cancellationToken) is null)
        {
            throw new PipeException($"'{container}/{path}' not found");
        }
        if (newContainer != container)
        {
            await EnsureContainerAsync(session, newContainer, parameters.CreateContainer, cancellationToken);
        }

        // Blob Storage has no rename: copy on the server, then delete
        await session.CopyAsync(container, path, newContainer, newPath, parameters.Overwrite, cancellationToken);
        await session.DeleteAsync(container, path, cancellationToken);

        var item = input.Clone();
        item.Json["previousContainer"] = container;
        item.Json["previousPath"] = path;
        item.Json["container"] = newContainer;
        item.Json["path"] = newPath;
        item.Json["fileName"] = BlobPaths.Name(newPath);
        return item;
    }

    static internal async Task EnsureContainerAsync(IPipeBlobSession session, string container, bool create, CancellationToken cancellationToken)
    {
        if (await session.ContainerExistsAsync(container, cancellationToken))
        {
            return;
        }
        if (!create)
        {
            throw new PipeException($"The container '{container}' doesn't exist; switch on Create container");
        }
        await session.CreateContainerAsync(container, cancellationToken);
    }

    static private string BinaryName(AzureBlobNodeParameters parameters)
        => string.IsNullOrWhiteSpace(parameters.BinaryProperty) ? "data" : parameters.BinaryProperty.Trim();
}

static internal class BlobPaths
{
    static public string Container(string? container, string label)
        => string.IsNullOrWhiteSpace(container) ? throw new PipeException($"{label} is required") : container.Trim().ToLowerInvariant();

    /// <summary>The blob name with '/' only and without a leading '/'.</summary>
    static public string Required(string? path, string label)
    {
        var normalized = Normalize(path);
        return normalized.Length == 0 ? throw new PipeException($"{label} is required") : normalized;
    }

    /// <summary>A folder as prefix: "orders" and "orders/" are "orders/"; empty is the whole container.</summary>
    static public string Prefix(string? path)
    {
        var normalized = Normalize(path).TrimEnd('/');
        return normalized.Length == 0 ? "" : normalized + "/";
    }

    static public string Name(string path) => path[(path.LastIndexOf('/') + 1)..];

    static private string Normalize(string? path) => (path ?? "").Trim().Replace('\\', '/').TrimStart('/');

    static public Regex PatternToRegex(string? pattern)
    {
        var text = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern.Trim();
        return new Regex("^" + Regex.Escape(text).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    static public JsonObject Describe(PipeBlobEntry blob)
    {
        var json = new JsonObject();
        Fill(json, blob);
        return json;
    }

    static public void Fill(JsonObject json, PipeBlobEntry blob)
    {
        json["fileName"] = blob.Name;
        json["container"] = blob.Container;
        json["path"] = blob.Path;
        json["size"] = blob.Size;
        json["modifiedAt"] = blob.ModifiedAt.ToUniversalTime().ToString("O");
        json["contentType"] = blob.ContentType;
        json["etag"] = blob.ETag;
    }
}
