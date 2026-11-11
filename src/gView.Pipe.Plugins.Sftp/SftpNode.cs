using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Sftp;

public enum SftpOperation
{
    [PipeEnumValue("One item per file in a folder, with name, size and date")]
    List,

    [PipeEnumValue("The content of a file as an attachment")]
    Download,

    [PipeEnumValue("An attachment of the item as a file on the server")]
    Upload,

    [PipeEnumValue("Remove a file")]
    Delete,

    [PipeEnumValue("Give a file a new name or move it")]
    Rename
}

public sealed class SftpNodeParameters
{
    [Required, PipeCredential(SftpCredential.TypeId)]
    [PipeParameter(Label = "Server", Order = 1, Expressions = false, Description = "The SFTP Server credential: host, login and host key fingerprint")]
    public string? CredentialId { get; set; }

    [PipeParameter(Label = "Operation", Order = 2, Expressions = false, Description = "What to do")]
    public SftpOperation Operation { get; set; } = SftpOperation.List;

    [Required, PipeParameter(Label = "Path", Order = 3, Placeholder = "/outgoing/orders.csv",
        Description = "Path on the server: the folder for List, e.g. /outgoing, otherwise the file, e.g. /outgoing/orders.csv")]
    public string Path { get; set; } = "";

    [PipeParameter(Label = "New path", Order = 4, ShowWhen = nameof(Operation), ShowWhenValues = ["rename"],
        Description = "Where the file goes, e.g. /done/orders.csv")]
    public string NewPath { get; set; } = "";

    [PipeParameter(Label = "File pattern", Order = 5, Expressions = false, Placeholder = "*.csv", ShowWhen = nameof(Operation), ShowWhenValues = ["list"],
        Description = "Which files, e.g. *.csv; capitals don't matter")]
    public string Pattern { get; set; } = "*";

    [PipeParameter(Label = "Include subfolders", Order = 6, Expressions = false, ShowWhen = nameof(Operation), ShowWhenValues = ["list"],
        Description = "List the files below the folder too")]
    public bool IncludeSubfolders { get; set; }

    [PipeParameter(Label = "Include folders", Order = 7, Expressions = false, Advanced = true, ShowWhen = nameof(Operation), ShowWhenValues = ["list"],
        Description = "Folders become items too")]
    public bool IncludeDirectories { get; set; }

    [PipeParameter(Label = "Overwrite", Order = 8, Expressions = false, ShowWhen = nameof(Operation), ShowWhenValues = ["upload", "rename"],
        Description = "Replace a file that exists; otherwise that is an error")]
    public bool Overwrite { get; set; }

    [PipeParameter(Label = "Create folders", Order = 9, Expressions = false, Advanced = true, ShowWhen = nameof(Operation), ShowWhenValues = ["upload", "rename"],
        Description = "Create the folders of the target that are missing")]
    public bool CreateFolders { get; set; }

    [PipeParameter(Label = "Attachment name", Order = 10, Expressions = false, Advanced = true, ShowWhen = nameof(Operation), ShowWhenValues = ["download", "upload"],
        Description = "Download: name of the new attachment. Upload: the attachment to upload. data if empty")]
    public string BinaryProperty { get; set; } = "data";

    [PipeParameter(Label = "Max file size (MB)", Order = 11, Expressions = false, Advanced = true, ShowWhen = nameof(Operation), ShowWhenValues = ["download"],
        Description = "Larger files are refused before they are loaded")]
    [Range(1, 10240)]
    public int MaxFileSizeMb { get; set; } = 100;

    [PipeParameter(Label = "Timeout (seconds)", Order = 12, Expressions = false, Advanced = true, Description = "Longest wait for connecting and for each operation")]
    [Range(1, 3600)]
    public int TimeoutSeconds { get; set; } = 30;
}

[PipeNode(SftpNode.NodeId, "SFTP",
    Category = "Files",
    Description = "Lists, downloads, uploads, deletes and renames files on an SFTP server. Runs once per input item over one connection. "
                  + "Content comes and goes as binary attachments of the items; Read File and Write File connect it to local folders. "
                  + "The server's host key fingerprint is part of the credential and is checked.")]
[PipeNodeExamples]
public sealed class SftpNode : IPipeNode<SftpNodeParameters>
{
    public const string NodeId = "2c85ff47-8cf5-4aec-9c08-04f38b56d20a";

    /// <summary>More entries than this in a List are an error, so nothing is missing silently.</summary>
    public const int MaxListEntries = 10000;

    private readonly ISftpConnector _connector;

    public SftpNode(ISftpConnector connector)
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
        var first = context.GetParameters<SftpNodeParameters>(items[0], 0);
        if (string.IsNullOrWhiteSpace(first.CredentialId))
        {
            throw new PipeException("Select the SFTP Server credential");
        }
        var credential = await context.GetCredentialAsync<SftpCredential>(first.CredentialId, cancellationToken);
        if (string.IsNullOrWhiteSpace(credential.HostKeyFingerprint))
        {
            throw new PipeException("The credential has no host key fingerprint; without it the server can't be checked");
        }

        var output = new List<PipeItem>();
        await using var connection = await _connector.ConnectAsync(credential, TimeSpan.FromSeconds(Math.Clamp(first.TimeoutSeconds, 1, 3600)), cancellationToken);

        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parameters = context.GetParameters<SftpNodeParameters>(items[i], i);
            context.Logger.LogDebug("SFTP {Operation} {Path}", parameters.Operation, parameters.Path);

            switch (parameters.Operation)
            {
                case SftpOperation.List:
                    output.AddRange(await ListAsync(connection, parameters, cancellationToken));
                    break;
                case SftpOperation.Download:
                    output.Add(await DownloadAsync(connection, context, items[i], parameters, cancellationToken));
                    break;
                case SftpOperation.Upload:
                    output.Add(await UploadAsync(connection, context, items[i], parameters, cancellationToken));
                    break;
                case SftpOperation.Delete:
                    output.Add(await DeleteAsync(connection, items[i], parameters, cancellationToken));
                    break;
                default:
                    output.Add(await RenameAsync(connection, items[i], parameters, cancellationToken));
                    break;
            }
        }

        return PipeNodeResult.FromMain(output);
    }

    static private async Task<List<PipeItem>> ListAsync(ISftpConnection connection, SftpNodeParameters parameters, CancellationToken cancellationToken)
    {
        var path = RemotePath.Required(parameters.Path, "Path");
        var entries = await connection.ListAsync(path, parameters.IncludeSubfolders, MaxListEntries, cancellationToken);
        var pattern = PatternToRegex(parameters.Pattern);

        return entries
            .Where(e => (parameters.IncludeDirectories || !e.IsDirectory) && pattern.IsMatch(e.Name))
            .OrderBy(e => e.Path, StringComparer.Ordinal)
            .Select(e => new PipeItem(Describe(e)))
            .ToList();
    }

    static private async Task<PipeItem> DownloadAsync(ISftpConnection connection, IPipeNodeContext context, PipeItem input, SftpNodeParameters parameters, CancellationToken cancellationToken)
    {
        var path = RemotePath.Required(parameters.Path, "Path");
        var entry = await connection.StatAsync(path, cancellationToken) ?? throw new PipeException($"'{path}' not found on the server");
        if (entry.IsDirectory)
        {
            throw new PipeException($"'{path}' is a folder; use List to get its files");
        }
        var limit = (long)Math.Clamp(parameters.MaxFileSizeMb, 1, 10240) * 1024 * 1024;
        if (entry.Size > limit)
        {
            throw new PipeException($"'{path}' is {entry.Size / 1024 / 1024} MB, more than the allowed {parameters.MaxFileSizeMb} MB");
        }

        // a temp file that goes away with the stream, so large files don't fill the memory
        await using var temp = new FileStream(System.IO.Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        await connection.DownloadAsync(path, temp, cancellationToken);
        temp.Position = 0;
        var reference = await context.BinaryStore.StoreAsync(temp, entry.Name, "application/octet-stream", cancellationToken);

        var item = input.Clone();
        Fill(item.Json, entry);
        item.Binary[BinaryName(parameters)] = reference;
        return item;
    }

    static private async Task<PipeItem> UploadAsync(ISftpConnection connection, IPipeNodeContext context, PipeItem input, SftpNodeParameters parameters, CancellationToken cancellationToken)
    {
        var path = RemotePath.Required(parameters.Path, "Path");
        var property = BinaryName(parameters);
        if (!input.Binary.TryGetValue(property, out var reference))
        {
            throw new PipeException($"The item has no attachment '{property}' to upload");
        }

        var existing = await connection.StatAsync(path, cancellationToken);
        if (existing is not null)
        {
            if (existing.IsDirectory)
            {
                throw new PipeException($"'{path}' is a folder");
            }
            if (!parameters.Overwrite)
            {
                throw new PipeException($"'{path}' exists on the server; switch on Overwrite to replace it");
            }
        }
        await EnsureFolderAsync(connection, RemotePath.Parent(path), parameters.CreateFolders, cancellationToken);

        // a reader never sees half a file: the content goes to a temporary name first
        var partPath = path + ".part";
        try
        {
            await using (var stream = await context.BinaryStore.OpenReadAsync(reference, cancellationToken))
            {
                await connection.UploadAsync(partPath, stream, cancellationToken);
            }
            if (existing is not null)
            {
                await connection.DeleteAsync(path, cancellationToken);
            }
            await connection.RenameAsync(partPath, path, cancellationToken);
        }
        catch
        {
            await TryDeleteAsync(connection, partPath);
            throw;
        }

        var item = input.Clone();
        item.Json["path"] = path;
        item.Json["fileName"] = RemotePath.Name(path);
        item.Json["size"] = reference.Length;
        return item;
    }

    static private async Task<PipeItem> DeleteAsync(ISftpConnection connection, PipeItem input, SftpNodeParameters parameters, CancellationToken cancellationToken)
    {
        var path = RemotePath.Required(parameters.Path, "Path");
        var entry = await connection.StatAsync(path, cancellationToken) ?? throw new PipeException($"'{path}' not found on the server");
        if (entry.IsDirectory)
        {
            throw new PipeException($"'{path}' is a folder; only files are deleted");
        }

        await connection.DeleteAsync(path, cancellationToken);
        var item = input.Clone();
        item.Json["path"] = path;
        item.Json["deleted"] = true;
        return item;
    }

    static private async Task<PipeItem> RenameAsync(ISftpConnection connection, PipeItem input, SftpNodeParameters parameters, CancellationToken cancellationToken)
    {
        var path = RemotePath.Required(parameters.Path, "Path");
        var newPath = RemotePath.Required(parameters.NewPath, "New path");
        if (await connection.StatAsync(path, cancellationToken) is null)
        {
            throw new PipeException($"'{path}' not found on the server");
        }

        if (await connection.StatAsync(newPath, cancellationToken) is { } target)
        {
            if (target.IsDirectory)
            {
                throw new PipeException($"'{newPath}' is a folder");
            }
            if (!parameters.Overwrite)
            {
                throw new PipeException($"'{newPath}' exists on the server; switch on Overwrite to replace it");
            }
            await connection.DeleteAsync(newPath, cancellationToken);
        }
        await EnsureFolderAsync(connection, RemotePath.Parent(newPath), parameters.CreateFolders, cancellationToken);

        await connection.RenameAsync(path, newPath, cancellationToken);
        var item = input.Clone();
        item.Json["previousPath"] = path;
        item.Json["path"] = newPath;
        item.Json["fileName"] = RemotePath.Name(newPath);
        return item;
    }

    // folders from the top down; a missing one is an error unless it may be created
    static internal async Task EnsureFolderAsync(ISftpConnection connection, string folder, bool create, CancellationToken cancellationToken)
    {
        if (folder is "" or "/")
        {
            return;
        }

        if (await connection.StatAsync(folder, cancellationToken) is { } existing)
        {
            if (!existing.IsDirectory)
            {
                throw new PipeException($"'{folder}' is a file, not a folder");
            }
            return;
        }
        if (!create)
        {
            throw new PipeException($"The folder '{folder}' doesn't exist on the server; switch on Create folders");
        }

        await EnsureFolderAsync(connection, RemotePath.Parent(folder), create, cancellationToken);
        await connection.CreateFolderAsync(folder, cancellationToken);
    }

    static private async Task TryDeleteAsync(ISftpConnection connection, string path)
    {
        try
        {
            if (await connection.StatAsync(path, CancellationToken.None) is not null)
            {
                await connection.DeleteAsync(path, CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // the first error is the one that counts
        }
    }

    static private string BinaryName(SftpNodeParameters parameters)
        => string.IsNullOrWhiteSpace(parameters.BinaryProperty) ? "data" : parameters.BinaryProperty.Trim();

    static private JsonObject Describe(SftpEntry entry)
    {
        var json = new JsonObject();
        Fill(json, entry);
        json["isDirectory"] = entry.IsDirectory;
        return json;
    }

    static private void Fill(JsonObject json, SftpEntry entry)
    {
        json["fileName"] = entry.Name;
        json["path"] = entry.Path;
        json["size"] = entry.Size;
        json["modifiedAt"] = entry.ModifiedAt.ToUniversalTime().ToString("O");
    }

    static internal Regex PatternToRegex(string? pattern)
    {
        var text = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern.Trim();
        return new Regex("^" + Regex.Escape(text).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}

static internal class RemotePath
{
    /// <summary>The path with '/' only; throws if it is empty.</summary>
    static public string Required(string? path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new PipeException($"{label} is required");
        }
        if (path.Contains('\0'))
        {
            throw new PipeException($"{label} contains an invalid character");
        }
        return path.Trim().Replace('\\', '/');
    }

    static public string Parent(string path)
    {
        var trimmed = path.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? "" : slash == 0 ? "/" : trimmed[..slash];
    }

    static public string Name(string path) => path.TrimEnd('/')[(path.TrimEnd('/').LastIndexOf('/') + 1)..];
}
