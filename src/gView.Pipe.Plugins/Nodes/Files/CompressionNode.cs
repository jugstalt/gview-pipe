using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Files;

public enum CompressionOperation
{
    Zip,
    Unzip,
    Gzip,
    Gunzip
}

public enum ZipScope
{
    /// <summary>One archive per item with its attachments.</summary>
    EachItem,

    /// <summary>One archive with the attachments of all items.</summary>
    AllItems
}

public enum UnzipOutput
{
    /// <summary>One item per entry with the file in <c>data</c>.</summary>
    ItemPerEntry,

    /// <summary>The input item with every entry as an attachment.</summary>
    Attachments
}

public sealed class CompressionNodeParameters
{
    [PipeParameter(Label = "Operation", Order = 1, Expressions = false)]
    public CompressionOperation Operation { get; set; } = CompressionOperation.Zip;

    [PipeParameter(Label = "Binary properties", Order = 2, Expressions = false, Placeholder = "all",
        Description = "Zip: attachments to pack, separated by commas; empty packs all. Unzip, GZip, GUnzip: the attachment to read.")]
    public string? BinaryProperty { get; set; }

    [PipeParameter(Label = "Zip", Order = 3, Expressions = false, Description = "One archive per item, or one of all items")]
    public ZipScope Scope { get; set; } = ZipScope.EachItem;

    [PipeParameter(Label = "Archive name", Order = 4, Placeholder = "archive.zip", Description = "Zip: file name of the archive")]
    public string? FileName { get; set; }

    [PipeParameter(Label = "Unzip into", Order = 5, Expressions = false, Description = "One item per entry, or all entries as attachments of the input item")]
    public UnzipOutput Output { get; set; } = UnzipOutput.ItemPerEntry;

    [PipeParameter(Label = "Max size (MB)", Order = 6, Expressions = false,
        Description = "Unzip, GUnzip: stop when the unpacked content gets larger, against archives that unpack to huge sizes")]
    public int MaxMegabytes { get; set; } = 500;

    [PipeParameter(Label = "Output property", Order = 7, Expressions = false, Placeholder = "data",
        Description = "Zip, GZip, GUnzip: attachment for the result")]
    public string? OutputProperty { get; set; }
}

[PipeNode(PipeBuiltInNodes.Compression, "Compression",
    Category = "Files",
    Description = "Packs attachments into a ZIP archive and unpacks ZIP archives into items or attachments; compresses and decompresses single attachments with GZip.")]
public sealed class CompressionNode : IPipeNode<CompressionNodeParameters>
{
    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput();
        if (items.Count == 0)
        {
            return PipeNodeResult.FromMain([]);
        }

        var first = context.GetParameters<CompressionNodeParameters>(items[0]);
        if (first.Operation == CompressionOperation.Zip && first.Scope == ZipScope.AllItems)
        {
            var item = new PipeItem(new JsonObject { ["count"] = items.Count });
            await ZipAsync(context, first, items, item, cancellationToken);
            return PipeNodeResult.FromMain([item]);
        }

        var output = new List<PipeItem>();
        for (var i = 0; i < items.Count; i++)
        {
            var parameters = i == 0 ? first : context.GetParameters<CompressionNodeParameters>(items[i], i);
            switch (parameters.Operation)
            {
                case CompressionOperation.Zip:
                    var zipped = items[i].Clone();
                    await ZipAsync(context, parameters, [items[i]], zipped, cancellationToken);
                    output.Add(zipped);
                    break;
                case CompressionOperation.Unzip:
                    output.AddRange(await UnzipAsync(context, parameters, items[i], cancellationToken));
                    break;
                default:
                    output.Add(await GzipAsync(context, parameters, items[i], cancellationToken));
                    break;
            }
        }
        return PipeNodeResult.FromMain(output);
    }

    static private async Task ZipAsync(IPipeNodeContext context, CompressionNodeParameters parameters, IReadOnlyList<PipeItem> items, PipeItem target, CancellationToken cancellationToken)
    {
        var wanted = PipeFileFormatNodes.List(parameters.BinaryProperty);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var buffer = new MemoryStream();
        var count = 0;

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in items)
            {
                foreach (var (property, reference) in item.Binary.Where(b => wanted is null || wanted.Contains(b.Key)))
                {
                    var entry = archive.CreateEntry(UniqueName(names, string.IsNullOrWhiteSpace(reference.FileName) ? property : reference.FileName), CompressionLevel.Optimal);
                    await using var source = await context.BinaryStore.OpenReadAsync(reference, cancellationToken);
                    await using var destination = entry.Open();
                    await source.CopyToAsync(destination, cancellationToken);
                    count++;
                }
            }
        }
        if (count == 0)
        {
            throw new PipeException("There are no attachments to zip" + (wanted is null ? "" : $" named {string.Join(", ", wanted)}"));
        }

        buffer.Position = 0;
        var fileName = string.IsNullOrWhiteSpace(parameters.FileName) ? "archive.zip" : parameters.FileName.Trim();
        target.Binary[OutputProperty(parameters)] = await context.BinaryStore.StoreAsync(buffer, fileName, "application/zip", cancellationToken);
        target.Json["entries"] = count;
    }

    static private string UniqueName(HashSet<string> names, string name)
    {
        var candidate = name;
        for (var n = 2; !names.Add(candidate); n++)
        {
            candidate = $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}";
        }
        return candidate;
    }

    /// <summary>Entries are read into the binary store only; their paths never touch the file system.</summary>
    static private async Task<List<PipeItem>> UnzipAsync(IPipeNodeContext context, CompressionNodeParameters parameters, PipeItem item, CancellationToken cancellationToken)
    {
        var reference = PipeFileFormatNodes.Attachment(item, SingleProperty(parameters));
        var limit = Limit(parameters);
        long total = 0;
        var result = new List<PipeItem>();
        var attachments = parameters.Output == UnzipOutput.Attachments ? item.Clone() : null;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var content = await context.BinaryStore.OpenReadAsync(reference, cancellationToken);
        using var archive = await OpenArchiveAsync(content, cancellationToken);
        foreach (var entry in archive.Entries.Where(e => !e.FullName.EndsWith('/')))
        {
            await using var source = entry.Open();
            var stored = await StoreLimitedAsync(context, source, entry.Name, limit - total, cancellationToken);
            total += stored.Length;

            if (attachments is not null)
            {
                attachments.Binary[UniqueName(names, entry.Name)] = stored;
                continue;
            }
            var entryItem = new PipeItem(new JsonObject
            {
                ["fileName"] = entry.Name,
                ["path"] = entry.FullName,
                ["size"] = stored.Length,
                ["modifiedAt"] = entry.LastWriteTime.ToString("O"),
                ["archive"] = reference.FileName
            });
            entryItem.Binary["data"] = stored;
            result.Add(entryItem);
        }

        if (attachments is not null)
        {
            attachments.Json["entries"] = attachments.Binary.Count - item.Binary.Count;
            return [attachments];
        }
        return result;
    }

    static private async Task<ZipArchive> OpenArchiveAsync(Stream content, CancellationToken cancellationToken)
    {
        // ZipArchive needs a seekable stream
        var seekable = content.CanSeek ? content : await CopyAsync(content, cancellationToken);
        try
        {
            return new ZipArchive(seekable, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            throw new PipeException($"The attachment isn't a valid ZIP archive: {ex.Message}");
        }
    }

    static private async Task<MemoryStream> CopyAsync(Stream content, CancellationToken cancellationToken)
    {
        var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        copy.Position = 0;
        return copy;
    }

    static private async Task<PipeItem> GzipAsync(IPipeNodeContext context, CompressionNodeParameters parameters, PipeItem item, CancellationToken cancellationToken)
    {
        var property = SingleProperty(parameters);
        var reference = PipeFileFormatNodes.Attachment(item, property);
        var result = item.Clone();
        var target = string.IsNullOrWhiteSpace(parameters.OutputProperty) ? property : parameters.OutputProperty.Trim();

        await using var source = await context.BinaryStore.OpenReadAsync(reference, cancellationToken);
        if (parameters.Operation == CompressionOperation.Gzip)
        {
            using var buffer = new MemoryStream();
            await using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            {
                await source.CopyToAsync(gzip, cancellationToken);
            }
            buffer.Position = 0;
            result.Binary[target] = await context.BinaryStore.StoreAsync(buffer, reference.FileName + ".gz", "application/gzip", cancellationToken);
        }
        else
        {
            var name = reference.FileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? reference.FileName[..^3] : reference.FileName;
            try
            {
                await using var gunzip = new GZipStream(source, CompressionMode.Decompress);
                result.Binary[target] = await StoreLimitedAsync(context, gunzip, name, Limit(parameters), cancellationToken);
            }
            catch (InvalidDataException ex)
            {
                throw new PipeException($"The attachment isn't valid GZip: {ex.Message}");
            }
        }
        return result;
    }

    /// <summary>Copies at most <paramref name="remaining"/> bytes; more fails, so a small archive can't fill the disk.</summary>
    static private async Task<PipeBinaryRef> StoreLimitedAsync(IPipeNodeContext context, Stream source, string fileName, long remaining, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > remaining)
            {
                throw new PipeException("The unpacked content is larger than the allowed size (Max size)");
            }
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        return await context.BinaryStore.StoreAsync(buffer, fileName, PipeMimeTypes.FromFileName(fileName), cancellationToken);
    }

    static private long Limit(CompressionNodeParameters parameters) => Math.Max(1, parameters.MaxMegabytes) * 1024L * 1024L;

    static private string SingleProperty(CompressionNodeParameters parameters)
        => string.IsNullOrWhiteSpace(parameters.BinaryProperty) ? "data" : parameters.BinaryProperty.Trim();

    static private string OutputProperty(CompressionNodeParameters parameters)
        => string.IsNullOrWhiteSpace(parameters.OutputProperty) ? "data" : parameters.OutputProperty.Trim();
}
