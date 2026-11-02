using gView.Pipe.Abstraction.Audit;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;

namespace gView.Pipe.Core.Storage;

/// <summary>Logs every entry (category <c>gView.Pipe.Audit</c>), so a central log system keeps it too (ADR 0022).</summary>
internal abstract class PipeAuditLogBase : IPipeAuditLog
{
    private readonly ILogger _logger;

    protected PipeAuditLogBase(ILoggerFactory loggers)
    {
        _logger = loggers.CreateLogger("gView.Pipe.Audit");
    }

    public async Task WriteAsync(PipeAuditEntry entry, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("{Actor} {Area} {Action} {ObjectName} ({ObjectId}) {Details}",
            entry.Actor, entry.Area, entry.Action, entry.ObjectName, entry.ObjectId, entry.Details);
        await AppendAsync(entry, cancellationToken);
    }

    protected abstract Task AppendAsync(PipeAuditEntry entry, CancellationToken cancellationToken);

    public abstract Task<IReadOnlyList<PipeAuditEntry>> QueryAsync(PipeAuditQuery query, CancellationToken cancellationToken = default);

    public abstract Task PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}

internal sealed class PipeMemoryAuditLog : PipeAuditLogBase
{
    private readonly List<PipeAuditEntry> _entries = new();

    public PipeMemoryAuditLog(ILoggerFactory loggers) : base(loggers) { }

    protected override Task AppendAsync(PipeAuditEntry entry, CancellationToken cancellationToken)
    {
        lock (_entries)
        {
            _entries.Add(PipeJson.Clone(entry));
        }
        return Task.CompletedTask;
    }

    public override Task<IReadOnlyList<PipeAuditEntry>> QueryAsync(PipeAuditQuery query, CancellationToken cancellationToken = default)
    {
        lock (_entries)
        {
            return Task.FromResult<IReadOnlyList<PipeAuditEntry>>(_entries
                .Where(query.Matches)
                .OrderByDescending(e => e.Timestamp)
                .Take(query.MaxCount)
                .Select(PipeJson.Clone)
                .ToList());
        }
    }

    public override Task PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        lock (_entries)
        {
            _entries.RemoveAll(e => e.Timestamp < olderThan);
        }
        return Task.CompletedTask;
    }
}

/// <summary>One file of JSON lines per month: <c>{root}/audit/yyyy-MM.jsonl</c>, only appended.</summary>
internal sealed class PipeFileAuditLog : PipeAuditLogBase
{
    static private readonly JsonSerializerOptions Line = new(PipeJson.Options) { WriteIndented = false };

    private readonly string _folder;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PipeFileAuditLog(IOptions<PipeFileStoreOptions> options, ILoggerFactory loggers) : base(loggers)
    {
        _folder = Path.Combine(Path.GetFullPath(options.Value.RootPath), "audit");
    }

    protected override async Task AppendAsync(PipeAuditEntry entry, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(entry, Line) + "\n";
        await _lock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_folder);
            await File.AppendAllTextAsync(FileOf(entry.Timestamp), line, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public override async Task<IReadOnlyList<PipeAuditEntry>> QueryAsync(PipeAuditQuery query, CancellationToken cancellationToken = default)
    {
        var result = new List<PipeAuditEntry>();
        // month files newest first; a month before From can't have matches
        foreach (var file in Months().Where(f => query.From is null || string.CompareOrdinal(MonthOf(f), Month(query.From.Value)) >= 0))
        {
            if (query.Before is { } before && string.CompareOrdinal(MonthOf(file), Month(before)) > 0)
            {
                continue;
            }

            string[] lines;
            await _lock.WaitAsync(cancellationToken);
            try
            {
                lines = await File.ReadAllLinesAsync(file, cancellationToken);
            }
            finally
            {
                _lock.Release();
            }

            for (var i = lines.Length - 1; i >= 0 && result.Count < query.MaxCount; i--)
            {
                if (lines[i].Length > 0 && Parse(lines[i]) is { } entry && query.Matches(entry))
                {
                    result.Add(entry);
                }
            }
            if (result.Count >= query.MaxCount)
            {
                break;
            }
        }
        return result;
    }

    public override async Task PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        // whole months only: a file goes when its newest possible entry is older than the limit
        var limit = Month(olderThan);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            foreach (var file in Months().Where(f => string.CompareOrdinal(MonthOf(f), limit) < 0))
            {
                File.Delete(file);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    static private PipeAuditEntry? Parse(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<PipeAuditEntry>(line, PipeJson.Options);
        }
        catch (JsonException)
        {
            // a line cut off by a crash; the others stay readable
            return null;
        }
    }

    private IEnumerable<string> Months()
        => Directory.Exists(_folder)
            ? Directory.GetFiles(_folder, "*.jsonl").OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            : [];

    private string FileOf(DateTimeOffset time) => Path.Combine(_folder, Month(time) + ".jsonl");

    static private string Month(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    static private string MonthOf(string file) => Path.GetFileNameWithoutExtension(file);
}
