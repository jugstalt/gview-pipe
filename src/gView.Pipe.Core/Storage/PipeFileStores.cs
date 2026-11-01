using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Options;
using gView.Pipe.Abstraction.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Storage;

/// <summary>One JSON file per workflow: <c>{root}/workflows/{id}.json</c>.</summary>
internal sealed class PipeFileWorkflowStore : IPipeWorkflowStore
{
    private readonly string _folder;
    private readonly string? _seedFolder;
    private readonly string? _seedGroups;
    private readonly string _groupsFile;
    private readonly PipeSecretValues _secrets;
    private readonly ILogger<PipeFileWorkflowStore> _logger;
    private readonly Lock _seedLock = new();
    private bool _seeded;

    public PipeFileWorkflowStore(IOptions<PipeFileStoreOptions> options, PipeSecretValues secrets, ILogger<PipeFileWorkflowStore> logger)
    {
        _secrets = secrets;
        _folder = Path.Combine(Path.GetFullPath(options.Value.RootPath), "workflows");
        _seedFolder = options.Value.SeedWorkflowsPath is { } seed ? Path.GetFullPath(seed) : null;
        _seedGroups = options.Value.SeedGroupsPath is { } groups ? Path.GetFullPath(groups) : null;
        _groupsFile = PipeFileWorkflowGroupStore.FileOf(options.Value);
        _logger = logger;
    }

    public async Task<IReadOnlyList<PipeWorkflow>> ListAsync(CancellationToken cancellationToken = default)
    {
        EnsureSeeded();
        if (!Directory.Exists(_folder))
        {
            return [];
        }

        var workflows = new List<PipeWorkflow>();
        foreach (var file in Directory.GetFiles(_folder, "*.json"))
        {
            try
            {
                if (PipeJson.Deserialize<PipeWorkflow>(await File.ReadAllTextAsync(file, cancellationToken)) is { } workflow)
                {
                    _secrets.Unprotect(workflow.Variables);
                    workflows.Add(workflow);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Can't read workflow {File}", file);
            }
        }

        return workflows.OrderBy(w => w.Name).ToList();
    }

    public async Task<PipeWorkflow?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        EnsureSeeded();
        var path = PathOf(id);
        var workflow = File.Exists(path)
            ? PipeJson.Deserialize<PipeWorkflow>(await File.ReadAllTextAsync(path, cancellationToken))
            : null;
        _secrets.Unprotect(workflow?.Variables);
        return workflow;
    }

    public Task SaveAsync(PipeWorkflow workflow, CancellationToken cancellationToken = default)
    {
        EnsureSeeded();
        PipeStoreIds.Prepare(workflow);
        var stored = workflow;
        if (workflow.Variables?.Any(v => v.Secret) == true)
        {
            stored = PipeJson.Clone(workflow);
            stored.Variables = _secrets.Protected(workflow.Variables);
        }
        return PipeFiles.WriteAtomicAsync(PathOf(workflow.Id), PipeJson.Serialize(stored), cancellationToken);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        EnsureSeeded();
        File.Delete(PathOf(id));
        return Task.CompletedTask;
    }

    private string PathOf(string id) => Path.Combine(_folder, $"{PipeStoreIds.Validate(id)}.json");

    /// <summary>Copies the sample workflows once, and only into a store without any workflow.</summary>
    private void EnsureSeeded()
    {
        lock (_seedLock)
        {
            if (_seeded)
            {
                return;
            }
            _seeded = true;

            if (_seedFolder is null || !Directory.Exists(_seedFolder)
                || (Directory.Exists(_folder) && Directory.EnumerateFiles(_folder, "*.json").Any()))
            {
                return;
            }

            Directory.CreateDirectory(_folder);
            foreach (var file in Directory.GetFiles(_seedFolder, "*.json"))
            {
                File.Copy(file, Path.Combine(_folder, Path.GetFileName(file)));
                _logger.LogInformation("Seeded workflow {File}", Path.GetFileName(file));
            }

            // the groups the samples are in, unless the store already has groups
            if (_seedGroups is not null && File.Exists(_seedGroups) && !File.Exists(_groupsFile))
            {
                File.Copy(_seedGroups, _groupsFile);
                _logger.LogInformation("Seeded workflow groups");
            }
        }
    }
}

/// <summary>All groups in one JSON file, <c>{root}/groups.json</c>; there are few, and they change rarely.</summary>
internal sealed class PipeFileWorkflowGroupStore : IPipeWorkflowGroupStore
{
    private readonly string _file;
    private readonly PipeSecretValues _secrets;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PipeFileWorkflowGroupStore(IOptions<PipeFileStoreOptions> options, PipeSecretValues secrets)
    {
        _file = FileOf(options.Value);
        _secrets = secrets;
    }

    static public string FileOf(PipeFileStoreOptions options) => Path.Combine(Path.GetFullPath(options.RootPath), "groups.json");

    public async Task<IReadOnlyList<PipeWorkflowGroup>> ListAsync(CancellationToken cancellationToken = default)
        => (await ReadAsync(cancellationToken)).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public async Task<PipeWorkflowGroup?> GetAsync(string id, CancellationToken cancellationToken = default)
        => (await ReadAsync(cancellationToken)).FirstOrDefault(g => g.Id == id);

    public async Task SaveAsync(PipeWorkflowGroup group, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var groups = await ReadAsync(cancellationToken);
            PipeStoreIds.Prepare(group, groups);
            groups.RemoveAll(g => g.Id == group.Id);
            groups.Add(group);
            await WriteAsync(groups, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var groups = await ReadAsync(cancellationToken);
            if (groups.RemoveAll(g => g.Id == id) > 0)
            {
                await WriteAsync(groups, cancellationToken);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<PipeWorkflowGroup>> ReadAsync(CancellationToken cancellationToken)
    {
        var groups = File.Exists(_file)
            ? PipeJson.Deserialize<List<PipeWorkflowGroup>>(await File.ReadAllTextAsync(_file, cancellationToken)) ?? []
            : [];
        foreach (var group in groups)
        {
            _secrets.Unprotect(group.Variables);
        }
        return groups;
    }

    private Task WriteAsync(List<PipeWorkflowGroup> groups, CancellationToken cancellationToken)
    {
        var stored = groups.Select(g =>
        {
            if (g.Variables?.Any(v => v.Secret) != true)
            {
                return g;
            }
            var copy = PipeJson.Clone(g);
            copy.Variables = _secrets.Protected(g.Variables);
            return copy;
        }).ToList();
        return PipeFiles.WriteAtomicAsync(_file, PipeJson.Serialize(stored), cancellationToken);
    }
}

/// <summary>
/// One JSON file per execution: <c>{root}/executions/{workflowId}/{startedAt}_{executionId}.json</c>.
/// The timestamp prefix keeps listing and pruning cheap.
/// </summary>
internal sealed class PipeFileExecutionStore : IPipeExecutionStore
{
    private readonly string _folder;

    public PipeFileExecutionStore(IOptions<PipeFileStoreOptions> options)
    {
        _folder = Path.Combine(Path.GetFullPath(options.Value.RootPath), "executions");
    }

    public Task SaveAsync(PipeExecution execution, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(FolderOf(execution.WorkflowId), $"{execution.StartedAt.UtcDateTime:yyyyMMddHHmmssfff}_{PipeStoreIds.Validate(execution.Id)}.json");
        return PipeFiles.WriteAtomicAsync(path, PipeJson.Serialize(execution), cancellationToken);
    }

    public async Task<PipeExecution?> GetAsync(string workflowId, string executionId, CancellationToken cancellationToken = default)
    {
        var folder = FolderOf(workflowId);
        var file = Directory.Exists(folder)
            ? Directory.GetFiles(folder, $"*_{PipeStoreIds.Validate(executionId)}.json").FirstOrDefault()
            : null;

        return file is null ? null : PipeJson.Deserialize<PipeExecution>(await File.ReadAllTextAsync(file, cancellationToken));
    }

    public async Task<IReadOnlyList<PipeExecution>> ListAsync(string workflowId, int maxCount = 50, CancellationToken cancellationToken = default)
    {
        var executions = new List<PipeExecution>();
        foreach (var file in FilesNewestFirst(workflowId).Take(maxCount))
        {
            if (PipeJson.Deserialize<PipeExecution>(await File.ReadAllTextAsync(file, cancellationToken)) is { } execution)
            {
                executions.Add(execution);
            }
        }
        return executions;
    }

    public Task PruneAsync(string workflowId, int keep, CancellationToken cancellationToken = default)
    {
        foreach (var file in FilesNewestFirst(workflowId).Skip(keep))
        {
            File.Delete(file);
        }
        return Task.CompletedTask;
    }

    private IEnumerable<string> FilesNewestFirst(string workflowId)
    {
        var folder = FolderOf(workflowId);
        return Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            : [];
    }

    private string FolderOf(string workflowId) => Path.Combine(_folder, PipeStoreIds.Validate(workflowId));
}

/// <summary>
/// One JSON file per credential: <c>{root}/credentials/{id}.json</c>.
/// The credential data is encrypted with ASP.NET Data Protection.
/// </summary>
internal sealed class PipeFileCredentialStore : IPipeCredentialStore
{
    private const string ProtectorPurpose = "gView.Pipe.Credentials.v1";

    private readonly string _folder;
    private readonly IDataProtector _protector;

    public PipeFileCredentialStore(IOptions<PipeFileStoreOptions> options, IDataProtectionProvider dataProtection)
    {
        _folder = Path.Combine(Path.GetFullPath(options.Value.RootPath), "credentials");
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
    }

    public async Task<IReadOnlyList<PipeCredentialInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_folder))
        {
            return [];
        }

        var credentials = new List<PipeCredentialInfo>();
        foreach (var file in Directory.GetFiles(_folder, "*.json"))
        {
            if (PipeJson.Deserialize<StoredCredential>(await File.ReadAllTextAsync(file, cancellationToken)) is { } stored)
            {
                credentials.Add(new PipeCredentialInfo(stored.Id, stored.Name, stored.TypeId));
            }
        }
        return credentials.OrderBy(c => c.Name).ToList();
    }

    public async Task<PipeCredential?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var path = PathOf(id);
        if (!File.Exists(path) || PipeJson.Deserialize<StoredCredential>(await File.ReadAllTextAsync(path, cancellationToken)) is not { } stored)
        {
            return null;
        }

        return new PipeCredential
        {
            Id = stored.Id,
            Name = stored.Name,
            TypeId = stored.TypeId,
            Data = JsonNode.Parse(_protector.Unprotect(stored.ProtectedData)) as JsonObject ?? new JsonObject()
        };
    }

    public Task SaveAsync(PipeCredential credential, CancellationToken cancellationToken = default)
    {
        PipeStoreIds.Prepare(credential);

        var stored = new StoredCredential
        {
            Id = credential.Id,
            Name = credential.Name,
            TypeId = credential.TypeId,
            ProtectedData = _protector.Protect(credential.Data.ToJsonString())
        };

        return PipeFiles.WriteAtomicAsync(PathOf(credential.Id), PipeJson.Serialize(stored), cancellationToken);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        File.Delete(PathOf(id));
        return Task.CompletedTask;
    }

    private string PathOf(string id) => Path.Combine(_folder, $"{PipeStoreIds.Validate(id)}.json");

    private sealed class StoredCredential
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public Guid TypeId { get; set; }
        public string ProtectedData { get; set; } = "";
    }
}

static internal class PipeFiles
{
    /// <summary>Writes to a temp file first, so readers never see half-written JSON.</summary>
    static public async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temp, content, cancellationToken);
        File.Move(temp, path, overwrite: true);
    }
}
