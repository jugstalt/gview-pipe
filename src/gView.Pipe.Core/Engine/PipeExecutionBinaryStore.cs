using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using System.Collections.Concurrent;

namespace gView.Pipe.Core.Engine;

/// <summary>Binary store scoped to one execution; temp files are deleted when the execution ends.</summary>
internal sealed class PipeExecutionBinaryStore : IPipeBinaryStore, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, byte[]> _memory = new();
    private readonly ConcurrentDictionary<string, string> _files = new();
    private readonly long _memoryThreshold;
    private readonly string _tempFolder;

    public PipeExecutionBinaryStore(long memoryThreshold, string? tempRoot)
    {
        _memoryThreshold = memoryThreshold;
        _tempFolder = Path.Combine(tempRoot ?? Path.GetTempPath(), "gview-pipe", Guid.NewGuid().ToString("N"));
    }

    public async Task<PipeBinaryRef> StoreAsync(Stream content, string fileName, string mimeType, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        if (buffer.Length <= _memoryThreshold)
        {
            _memory[id] = buffer.ToArray();
        }
        else
        {
            Directory.CreateDirectory(_tempFolder);
            var path = Path.Combine(_tempFolder, id);
            buffer.Position = 0;
            await using (var file = File.Create(path))
            {
                await buffer.CopyToAsync(file, cancellationToken);
            }
            _files[id] = path;
        }

        return new PipeBinaryRef(id, fileName, mimeType, buffer.Length);
    }

    public Task<Stream> OpenReadAsync(PipeBinaryRef reference, CancellationToken cancellationToken = default)
    {
        if (_memory.TryGetValue(reference.Id, out var bytes))
        {
            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }
        if (_files.TryGetValue(reference.Id, out var path))
        {
            return Task.FromResult<Stream>(File.OpenRead(path));
        }

        throw new PipeException($"Binary '{reference.FileName}' ({reference.Id}) is not available in this execution");
    }

    public ValueTask DisposeAsync()
    {
        _memory.Clear();
        try
        {
            if (Directory.Exists(_tempFolder))
            {
                Directory.Delete(_tempFolder, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return ValueTask.CompletedTask;
    }
}
