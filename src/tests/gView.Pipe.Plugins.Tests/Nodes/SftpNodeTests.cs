using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.Sftp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text;

namespace gView.Pipe.Plugins.Tests.Nodes;

public sealed class SftpNodeTests
{
    /// <summary>A server in memory: files, folders and a log of what the node did.</summary>
    internal sealed class FakeServer : ISftpConnector
    {
        static private readonly DateTimeOffset Modified = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

        public Dictionary<string, byte[]> Files { get; } = new();
        public HashSet<string> Folders { get; } = new() { "/" };
        public List<string> Log { get; } = new();
        public int Connections { get; private set; }
        public SftpCredential? Credential { get; private set; }
        public TimeSpan Timeout { get; private set; }
        public Dictionary<string, DateTimeOffset> Times { get; } = new();
        public bool FailRename { get; set; }

        /// <summary>Changes the modified time of a file, like a writer that is still at work.</summary>
        public void Touch(string path) => Times[path] = Times.GetValueOrDefault(path, Modified).AddMinutes(1);

        public FakeServer Add(string path, string content = "x")
        {
            Files[path] = Encoding.UTF8.GetBytes(content);
            for (var folder = RemotePathParent(path); folder.Length > 0 && Folders.Add(folder); folder = RemotePathParent(folder))
            {
            }
            return this;
        }

        public Task<ISftpConnection> ConnectAsync(SftpCredential credential, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Connections++;
            Credential = credential;
            Timeout = timeout;
            return Task.FromResult<ISftpConnection>(new Connection(this));
        }

        static public string RemotePathParent(string path)
        {
            var slash = path.TrimEnd('/').LastIndexOf('/');
            return slash <= 0 ? "" : path[..slash];
        }

        private sealed class Connection(FakeServer server) : ISftpConnection
        {
            private SftpEntry FileEntry(string path) => new(path, path[(path.LastIndexOf('/') + 1)..], server.Files[path].Length, server.Times.GetValueOrDefault(path, Modified), false);

            private SftpEntry FolderEntry(string path) => new(path, path[(path.LastIndexOf('/') + 1)..], 0, Modified, true);

            public Task<SftpEntry?> StatAsync(string path, CancellationToken cancellationToken)
                => Task.FromResult<SftpEntry?>(server.Files.ContainsKey(path) ? FileEntry(path) : server.Folders.Contains(path) ? FolderEntry(path) : null);

            public Task<IReadOnlyList<SftpEntry>> ListAsync(string path, bool includeSubfolders, int maxEntries, CancellationToken cancellationToken)
            {
                var prefix = path.TrimEnd('/') + "/";
                bool Below(string p) => p.StartsWith(prefix, StringComparison.Ordinal) && (includeSubfolders || p.IndexOf('/', prefix.Length) < 0);

                var entries = server.Files.Keys.Where(Below).Select(FileEntry)
                    .Concat(server.Folders.Where(f => f != "/" && Below(f)).Select(FolderEntry))
                    .ToList();
                return entries.Count > maxEntries
                    ? throw new PipeException($"'{path}' has more than {maxEntries} entries")
                    : Task.FromResult<IReadOnlyList<SftpEntry>>(entries);
            }

            public async Task DownloadAsync(string path, Stream destination, CancellationToken cancellationToken)
            {
                server.Log.Add($"download {path}");
                await destination.WriteAsync(server.Files[path], cancellationToken);
            }

            public async Task UploadAsync(string path, Stream source, CancellationToken cancellationToken)
            {
                server.Log.Add($"upload {path}");
                using var buffer = new MemoryStream();
                await source.CopyToAsync(buffer, cancellationToken);
                server.Files[path] = buffer.ToArray();
            }

            public Task CreateFolderAsync(string path, CancellationToken cancellationToken)
            {
                server.Log.Add($"mkdir {path}");
                server.Folders.Add(path);
                return Task.CompletedTask;
            }

            public Task DeleteAsync(string path, CancellationToken cancellationToken)
            {
                server.Log.Add($"delete {path}");
                server.Files.Remove(path);
                return Task.CompletedTask;
            }

            public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken)
            {
                server.Log.Add($"rename {path} {newPath}");
                if (server.FailRename)
                {
                    throw new PipeException("not allowed");
                }
                if (server.Files.ContainsKey(newPath))
                {
                    throw new PipeException("the target exists");
                }
                server.Files[newPath] = server.Files[path];
                server.Files.Remove(path);
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Setup : IDisposable
    {
        public FakeServer Server { get; } = new();
        public ServiceProvider Services { get; }
        public string CredentialId { get; private set; } = "";

        public Setup()
        {
            Services = PluginTestHost.Create(
                s => s.Replace(ServiceDescriptor.Singleton<ISftpConnector>(Server)),
                o => o.PluginAssemblies.Add(typeof(SftpNode).Assembly));
        }

        public async Task<Setup> WithCredentialAsync(SftpCredential? data = null)
        {
            var credential = new PipeCredential
            {
                Name = "Partner",
                TypeId = Guid.Parse(SftpCredential.TypeId),
                Data = PipeJson.ToJsonObject(data ?? new SftpCredential
                {
                    Host = "sftp.example.test",
                    UserName = "pipe",
                    Password = "pw",
                    HostKeyFingerprint = "SHA256:abc"
                })
            };
            await Services.GetRequiredService<IPipeCredentialStore>().SaveAsync(credential);
            CredentialId = credential.Id;
            return this;
        }

        /// <summary>start → sftp (→ sftp) with the given operations; returns the execution.</summary>
        public async Task<PipeExecution> RunAsync(params object[] operations)
        {
            var builder = new PipeWorkflowBuilder().Node("start", PipeBuiltInNodes.ManualTrigger);
            var previous = "start";
            for (var i = 0; i < operations.Length; i++)
            {
                var id = $"sftp{i}";
                var json = PipeJson.ToJsonObject(operations[i]);
                json["credentialId"] = CredentialId;
                builder = builder.Node(id, SftpNode.NodeId, json).Connect(previous, id);
                previous = id;
            }
            return await Services.GetRequiredService<IPipeEngine>().ExecuteAsync(builder.Build());
        }

        public void Dispose() => Services.Dispose();
    }

    static private async Task<Setup> NewAsync(Action<FakeServer>? prepare = null)
    {
        var setup = new Setup();
        prepare?.Invoke(setup.Server);
        return await setup.WithCredentialAsync();
    }

    [Fact]
    public async Task List_returns_the_files_that_match_and_leaves_folders_out()
    {
        using var setup = await NewAsync(s => s.Add("/in/a.csv").Add("/in/B.CSV").Add("/in/c.txt").Add("/in/sub/d.csv"));

        var execution = await setup.RunAsync(new { operation = "list", path = "/in", pattern = "*.csv" });

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        var items = execution.Output("sftp0");
        Assert.Equal(["/in/B.CSV", "/in/a.csv"], items.Select(i => i["path"]!.GetValue<string>()));
        Assert.Equal("a.csv", items[1]["fileName"]!.GetValue<string>());
        Assert.False(items[1]["isDirectory"]!.GetValue<bool>());
        Assert.Equal(1, items[1]["size"]!.GetValue<long>());
    }

    [Fact]
    public async Task List_can_go_into_subfolders_and_include_folders()
    {
        using var setup = await NewAsync(s => s.Add("/in/a.csv").Add("/in/sub/d.csv"));

        var execution = await setup.RunAsync(new { operation = "list", path = "/in", includeSubfolders = true, includeDirectories = true });

        var paths = execution.Output("sftp0").Select(i => i["path"]!.GetValue<string>()).ToList();
        Assert.Equal(["/in/a.csv", "/in/sub", "/in/sub/d.csv"], paths);
    }

    [Fact]
    public async Task Download_attaches_the_content_and_Upload_writes_it_via_a_part_file()
    {
        using var setup = await NewAsync(s => s.Add("/in/a.txt", "hello"));

        var execution = await setup.RunAsync(
            new { operation = "download", path = "/in/a.txt" },
            new { operation = "upload", path = "/out/a.txt", createFolders = true });

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        var downloaded = execution.Output("sftp0").Single();
        Assert.Equal("a.txt", downloaded["fileName"]!.GetValue<string>());
        Assert.Equal("hello", Encoding.UTF8.GetString(setup.Server.Files["/out/a.txt"]));
        Assert.DoesNotContain("/out/a.txt.part", setup.Server.Files.Keys);
        Assert.Equal(["download /in/a.txt", "mkdir /out", "upload /out/a.txt.part", "rename /out/a.txt.part /out/a.txt"], setup.Server.Log);
        // one connection per node run: the download node and the upload node
        Assert.Equal(2, setup.Server.Connections);
    }

    [Fact]
    public async Task Upload_refuses_an_existing_file_and_a_missing_folder_unless_allowed()
    {
        using var setup = await NewAsync(s => s.Add("/in/a.txt", "new").Add("/out/a.txt", "old"));

        var exists = await setup.RunAsync(
            new { operation = "download", path = "/in/a.txt" },
            new { operation = "upload", path = "/out/a.txt" });
        Assert.Equal(PipeExecutionStatus.Error, exists.Status);
        Assert.Contains("Overwrite", exists.Error);
        Assert.Equal("old", Encoding.UTF8.GetString(setup.Server.Files["/out/a.txt"]));

        var folder = await setup.RunAsync(
            new { operation = "download", path = "/in/a.txt" },
            new { operation = "upload", path = "/nowhere/a.txt" });
        Assert.Equal(PipeExecutionStatus.Error, folder.Status);
        Assert.Contains("Create folders", folder.Error);

        var overwritten = await setup.RunAsync(
            new { operation = "download", path = "/in/a.txt" },
            new { operation = "upload", path = "/out/a.txt", overwrite = true });
        Assert.True(overwritten.Status == PipeExecutionStatus.Success, overwritten.Error);
        Assert.Equal("new", Encoding.UTF8.GetString(setup.Server.Files["/out/a.txt"]));
    }

    [Fact]
    public async Task Upload_without_an_attachment_is_an_error()
    {
        using var setup = await NewAsync();

        var execution = await setup.RunAsync(new { operation = "upload", path = "/out/a.txt" });

        Assert.Equal(PipeExecutionStatus.Error, execution.Status);
        Assert.Contains("no attachment", execution.Error);
    }

    [Fact]
    public async Task Download_refuses_a_file_that_is_too_large_a_folder_and_a_missing_file()
    {
        using var setup = await NewAsync(s => s.Add("/in/big.bin", new string('x', 2 * 1024 * 1024)).Add("/in/a.txt"));

        var tooLarge = await setup.RunAsync(new { operation = "download", path = "/in/big.bin", maxFileSizeMb = 1 });
        Assert.Equal(PipeExecutionStatus.Error, tooLarge.Status);
        Assert.Contains("more than the allowed 1 MB", tooLarge.Error);

        var folder = await setup.RunAsync(new { operation = "download", path = "/in" });
        Assert.Contains("is a folder", folder.Error);

        var missing = await setup.RunAsync(new { operation = "download", path = "/in/none.txt" });
        Assert.Contains("not found", missing.Error);
        Assert.DoesNotContain(setup.Server.Log, l => l.StartsWith("download"));
    }

    [Fact]
    public async Task Delete_removes_a_file_and_a_missing_one_is_an_error()
    {
        using var setup = await NewAsync(s => s.Add("/in/a.txt"));

        var deleted = await setup.RunAsync(new { operation = "delete", path = "/in/a.txt" });
        Assert.True(deleted.Status == PipeExecutionStatus.Success, deleted.Error);
        Assert.True(deleted.Output("sftp0").Single()["deleted"]!.GetValue<bool>());
        Assert.Empty(setup.Server.Files);

        var again = await setup.RunAsync(new { operation = "delete", path = "/in/a.txt" });
        Assert.Equal(PipeExecutionStatus.Error, again.Status);
    }

    [Fact]
    public async Task Rename_moves_a_file_and_needs_overwrite_for_an_existing_target()
    {
        using var setup = await NewAsync(s => s.Add("/in/a.txt", "a").Add("/archive/a.txt", "old"));

        var blocked = await setup.RunAsync(new { operation = "rename", path = "/in/a.txt", newPath = "/archive/a.txt" });
        Assert.Equal(PipeExecutionStatus.Error, blocked.Status);
        Assert.Contains("Overwrite", blocked.Error);

        var moved = await setup.RunAsync(new { operation = "rename", path = "/in/a.txt", newPath = "/archive/a.txt", overwrite = true });
        Assert.True(moved.Status == PipeExecutionStatus.Success, moved.Error);
        var item = moved.Output("sftp0").Single();
        Assert.Equal("/archive/a.txt", item["path"]!.GetValue<string>());
        Assert.Equal("/in/a.txt", item["previousPath"]!.GetValue<string>());
        Assert.Equal("a", Encoding.UTF8.GetString(setup.Server.Files["/archive/a.txt"]));
        Assert.DoesNotContain("/in/a.txt", setup.Server.Files.Keys);
    }

    [Fact]
    public async Task All_items_share_one_connection_and_the_timeout_comes_from_the_node()
    {
        using var setup = await NewAsync(s => s.Add("/in/a.txt").Add("/in/b.txt"));
        var workflow = new PipeWorkflowBuilder()
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("list", SftpNode.NodeId, new { credentialId = setup.CredentialId, operation = "list", path = "/in" })
            .Node("delete", SftpNode.NodeId, new { credentialId = setup.CredentialId, operation = "delete", path = "{{ $json.path }}", timeoutSeconds = 5 })
            .Connect("start", "list").Connect("list", "delete")
            .Build();

        var execution = await setup.Services.GetRequiredService<IPipeEngine>().ExecuteAsync(workflow);

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        Assert.Empty(setup.Server.Files);
        // one connection for the list and one for the two deletes
        Assert.Equal(2, setup.Server.Connections);
        Assert.Equal(TimeSpan.FromSeconds(5), setup.Server.Timeout);
    }

    [Fact]
    public async Task A_credential_without_a_fingerprint_is_refused_before_connecting()
    {
        using var setup = new Setup();
        await setup.WithCredentialAsync(new SftpCredential { Host = "h", UserName = "u", Password = "p", HostKeyFingerprint = "" });

        var execution = await setup.RunAsync(new { operation = "list", path = "/in" });

        Assert.Equal(PipeExecutionStatus.Error, execution.Status);
        Assert.Contains("fingerprint", execution.Error);
        Assert.Equal(0, setup.Server.Connections);
    }
}
