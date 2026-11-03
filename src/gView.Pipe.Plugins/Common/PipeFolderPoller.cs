namespace gView.Pipe.Plugins.Common;

/// <summary>A file the File Trigger reports (ADR 0036).</summary>
public sealed record PipeFolderChange(string FullPath, string Event, long Size, DateTime ModifiedUtc);

/// <summary>
/// Compares snapshots of a folder: a file is reported once its size and time are the same in two checks in a row
/// and it can be opened for reading. Keeps its state in memory only. <see cref="Poll"/> takes the snapshot,
/// <see cref="Commit"/> marks what the caller has handed on; what is not committed is offered again.
/// </summary>
public sealed class PipeFolderPoller
{
    /// <summary>At most this many files per run.</summary>
    public const int MaxPerRun = 100;

    /// <summary>Checks after which a file that never gets stable is logged.</summary>
    public const int UnstableWarningAfter = 30;

    private readonly string _directory;
    private readonly string _pattern;
    private readonly bool _includeSubfolders;
    private readonly bool _reportChanged;
    private readonly Action<string> _warn;

    private readonly Dictionary<string, Signature> _known = new(Comparer);
    private readonly Dictionary<string, Pending> _pending = new(Comparer);
    private bool _baselineTaken;
    private bool _unreadable;

    private readonly record struct Signature(long Size, DateTime ModifiedUtc);

    private sealed class Pending
    {
        public Signature Signature { get; set; }
        public int Checks { get; set; }
        public bool Warned { get; set; }
    }

    static private StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public PipeFolderPoller(string directory, string pattern, bool includeSubfolders, bool reportChanged, Action<string> warn)
    {
        _directory = directory;
        _pattern = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern.Trim();
        _includeSubfolders = includeSubfolders;
        _reportChanged = reportChanged;
        _warn = warn;
    }

    /// <summary>The first call only takes the baseline: files that exist now trigger nothing.</summary>
    public IReadOnlyList<PipeFolderChange> Poll()
    {
        var current = Snapshot();
        if (current is null)
        {
            return [];
        }

        if (!_baselineTaken)
        {
            _baselineTaken = true;
            foreach (var (path, signature) in current)
            {
                _known[path] = signature;
            }
            return [];
        }

        // gone files are forgotten, so one that comes back is new
        foreach (var gone in _known.Keys.Where(k => !current.ContainsKey(k)).ToList())
        {
            _known.Remove(gone);
        }
        foreach (var gone in _pending.Keys.Where(k => !current.ContainsKey(k)).ToList())
        {
            _pending.Remove(gone);
        }

        var ready = new List<PipeFolderChange>();
        foreach (var (path, signature) in current.OrderBy(c => c.Value.ModifiedUtc).ThenBy(c => c.Key, StringComparer.Ordinal))
        {
            var isKnown = _known.TryGetValue(path, out var knownSignature);
            if (isKnown && knownSignature == signature)
            {
                _pending.Remove(path);
                continue;
            }
            if (isKnown && !_reportChanged)
            {
                // changed, but only new files count
                _known[path] = signature;
                _pending.Remove(path);
                continue;
            }

            if (!_pending.TryGetValue(path, out var pending))
            {
                _pending[path] = new Pending { Signature = signature, Checks = 1 };
                continue;
            }

            pending.Checks++;
            if (pending.Signature != signature)
            {
                // still changing: the wait for two equal checks starts again
                pending.Signature = signature;
                WarnWhenUnstable(path, pending);
                continue;
            }

            if (ready.Count >= MaxPerRun)
            {
                // stable, but the run is full; it follows with the next check
                continue;
            }

            if (CanBeRead(path))
            {
                ready.Add(new PipeFolderChange(path, isKnown ? "changed" : "created", signature.Size, signature.ModifiedUtc));
            }
            else
            {
                WarnWhenUnstable(path, pending);
            }
        }

        return ready;
    }

    private void WarnWhenUnstable(string path, Pending pending)
    {
        if (!pending.Warned && pending.Checks >= UnstableWarningAfter)
        {
            pending.Warned = true;
            _warn($"The file {path} is still in use or changing after {pending.Checks} checks; it is not reported yet");
        }
    }

    /// <summary>The caller has started a run for these files; they are known from now on.</summary>
    public void Commit(IEnumerable<PipeFolderChange> changes)
    {
        foreach (var change in changes)
        {
            if (_pending.Remove(change.FullPath, out var pending))
            {
                _known[change.FullPath] = pending.Signature;
            }
        }
    }

    // null: the folder can't be read; the state stays as it is, and the log warns once per outage
    private Dictionary<string, Signature>? Snapshot()
    {
        try
        {
            var result = new Dictionary<string, Signature>(Comparer);
            foreach (var path in Directory.EnumerateFiles(_directory, _pattern, _includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var info = new FileInfo(path);
                    result[path] = new Signature(info.Length, info.LastWriteTimeUtc);
                }
                catch (IOException)
                {
                    // gone or locked between listing and reading; the next check sees it
                }
            }
            _unreadable = false;
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_unreadable)
            {
                _warn($"The folder {_directory} can't be read ({ex.Message}); the trigger waits for it");
            }
            _unreadable = true;
            return null;
        }
    }

    static private bool CanBeRead(string path)
    {
        try
        {
            // fails while a writer still has the file open
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
