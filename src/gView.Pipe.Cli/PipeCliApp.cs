using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Cli;

/// <summary>
/// The commands of <c>gview-pipe</c> (ADR 0026). Exit code 0: done; 1: the run of <c>gview-pipe run --wait</c> failed;
/// 2: wrong call, or the server refused it.
/// </summary>
static public class PipeCliApp
{
    public const int Success = 0, RunFailed = 1, Failure = 2;

    private const string Usage = """
        Usage: gview-pipe <command> [options]

        Connection (all commands): --url <address> (or PIPE_URL), --key <api key> (or PIPE_API_KEY)

        gview-pipe workflows list [--json]
        gview-pipe workflows get <id>
        gview-pipe workflows export <id> [--out <file|folder>]
        gview-pipe workflows deploy <file|folder>... [--dry-run] [--force]
        gview-pipe workflows delete <id>
        gview-pipe workflows activate <id>
        gview-pipe workflows deactivate <id>
        gview-pipe run <id> [--wait] [--input <file|->] [--environment <name>] [--timeout <seconds>] [--json]
        gview-pipe executions list [--workflow <id>] [--status <status>] [--limit <n>] [--json]
        gview-pipe executions get <workflowId> <id>
        gview-pipe executions cancel <workflowId> <id>
        gview-pipe executions replay <workflowId> <id> [--from <nodeId>]
        """;

    static private readonly HashSet<string> Flags = ["json", "wait", "dry-run", "force", "help"];

    static private readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <param name="http">For tests; without it a client for <c>--url</c> is created.</param>
    /// <param name="environment">Looks up an environment variable (<c>PIPE_URL</c>, <c>PIPE_API_KEY</c>).</param>
    static public async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, TextReader input, Func<string, string?> environment, HttpClient? http = null)
    {
        try
        {
            var call = Call.Parse(args);
            if (call.Has("help") || call.Positionals.Count == 0)
            {
                (call.Has("help") ? output : error).WriteLine(Usage);
                return call.Has("help") ? Success : Failure;
            }

            var url = call.Get("url") ?? environment("PIPE_URL");
            var ownsClient = http is null;
            if (http is null)
            {
                if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var address))
                {
                    throw new PipeCliException("No server address: use --url or the environment variable PIPE_URL (e.g. https://pipe.example.com)");
                }
                http = new HttpClient { BaseAddress = address };
            }

            try
            {
                var key = call.Get("key") ?? environment("PIPE_API_KEY");
                var context = new Context(new PipeCliClient(http, key), call, output, error, input);
                return await DispatchAsync(context);
            }
            finally
            {
                if (ownsClient)
                {
                    http.Dispose();
                }
            }
        }
        catch (PipeCliException ex)
        {
            error.WriteLine("error: " + ex.Message);
            return Failure;
        }
        catch (HttpRequestException ex)
        {
            error.WriteLine("error: the server could not be reached: " + ex.Message);
            return Failure;
        }
    }

    static private Task<int> DispatchAsync(Context c)
    {
        var p = c.Call.Positionals;
        return (p[0], p.Count > 1 ? p[1] : "") switch
        {
            ("workflows", "list") => ListWorkflowsAsync(c),
            ("workflows", "get") => GetWorkflowAsync(c, export: false),
            ("workflows", "export") => GetWorkflowAsync(c, export: true),
            ("workflows", "deploy") => DeployAsync(c),
            ("workflows", "delete") => SimpleAsync(c, 2, id => c.Client.DeleteAsync($"workflows/{Escape(id)}"), "deleted"),
            ("workflows", "activate") => SimpleAsync(c, 2, id => c.Client.PostAsync($"workflows/{Escape(id)}/activate"), "activated"),
            ("workflows", "deactivate") => SimpleAsync(c, 2, id => c.Client.PostAsync($"workflows/{Escape(id)}/deactivate"), "deactivated"),
            ("run", _) => RunWorkflowAsync(c),
            ("executions", "list") => ListExecutionsAsync(c),
            ("executions", "get") => ExecutionAsync(c, "", get: true),
            ("executions", "cancel") => ExecutionAsync(c, "cancel", get: false),
            ("executions", "replay") => ExecutionAsync(c, "replay", get: false),
            _ => throw new PipeCliException($"Unknown command '{string.Join(' ', p.Take(2))}'. Run 'gview-pipe --help'.")
        };
    }

    // ---- workflows ----

    static private async Task<int> ListWorkflowsAsync(Context c)
    {
        var response = Check(await c.Client.GetAsync("workflows"));
        if (c.Call.Has("json"))
        {
            c.Output.WriteLine(Print(response.Body));
            return Success;
        }

        WriteTable(c.Output, ["ID", "NAME", "GROUP", "ACTIVE"],
            (response.Body as JsonArray ?? []).Select(w => new[]
            {
                Text(w?["id"]), Text(w?["name"]), Text(w?["groupId"]), w?["active"]?.GetValue<bool>() == true ? "yes" : "no"
            }));
        return Success;
    }

    static private async Task<int> GetWorkflowAsync(Context c, bool export)
    {
        var id = c.Argument(2, "a workflow id");
        var response = Check(await c.Client.GetAsync($"workflows/{Escape(id)}"));

        if (export && c.Call.Get("out") is { } target)
        {
            // a folder gets <id>.json
            var file = Directory.Exists(target) || target.EndsWith('/') || target.EndsWith('\\') ? Path.Combine(target, id + ".json") : target;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            await File.WriteAllTextAsync(file, Print(response.Body) + Environment.NewLine);
            c.Output.WriteLine($"exported {id} to {file}");
        }
        else
        {
            c.Output.WriteLine(Print(response.Body));
        }
        return Success;
    }

    /// <summary>
    /// Creates or replaces workflows from files. An existing workflow is only replaced if it is unchanged since this
    /// command read it (If-Match), unless --force. --dry-run reports what would happen and changes nothing.
    /// </summary>
    static private async Task<int> DeployAsync(Context c)
    {
        var paths = c.Call.Positionals.Skip(2).ToList();
        if (paths.Count == 0)
        {
            throw new PipeCliException("Name at least one workflow file or folder");
        }

        var files = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                files.AddRange(Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase));
            }
            else if (File.Exists(path))
            {
                files.Add(path);
            }
            else
            {
                throw new PipeCliException($"'{path}' does not exist");
            }
        }

        var dryRun = c.Call.Has("dry-run");
        var failures = 0;
        foreach (var file in files)
        {
            try
            {
                var workflow = JsonNode.Parse(await File.ReadAllTextAsync(file)) as JsonObject
                    ?? throw new PipeCliException("not a workflow (a JSON object is expected)");
                var id = workflow["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new PipeCliException("the workflow has no \"id\"");
                }

                var existing = await c.Client.GetAsync($"workflows/{Escape(id)}");
                var exists = existing.Status != System.Net.HttpStatusCode.NotFound;
                if (exists)
                {
                    Check(existing);
                }
                if (dryRun)
                {
                    c.Output.WriteLine($"would {(exists ? "update" : "create")} {id} ({file})");
                    continue;
                }

                var response = await c.Client.PutAsync($"workflows/{Escape(id)}", workflow, exists && !c.Call.Has("force") ? existing.ETag : null);
                if ((int)response.Status == 412)
                {
                    throw new PipeCliException($"{id} was changed on the server since it was read; deploy again, or use --force to replace it");
                }
                Check(response);
                c.Output.WriteLine($"{(exists ? "updated" : "created")} {id}");
                if (response.Body?["warnings"] is JsonArray { Count: > 0 } warnings)
                {
                    foreach (var warning in warnings)
                    {
                        c.Error.WriteLine($"warning: {id}: {warning}");
                    }
                }
            }
            catch (Exception ex) when (ex is PipeCliException or JsonException or IOException)
            {
                failures++;
                c.Error.WriteLine($"error: {file}: {ex.Message}");
            }
        }
        return failures == 0 ? Success : Failure;
    }

    static private async Task<int> SimpleAsync(Context c, int index, Func<string, Task<PipeCliResponse>> send, string done)
    {
        var id = c.Argument(index, "a workflow id");
        Check(await send(id));
        c.Output.WriteLine($"{done} {id}");
        return Success;
    }

    // ---- runs and executions ----

    static private async Task<int> RunWorkflowAsync(Context c)
    {
        var id = c.Argument(1, "a workflow id");
        var body = new JsonObject();
        if (c.Call.Get("input") is { } input)
        {
            var text = input == "-" ? await c.Input.ReadToEndAsync() : await File.ReadAllTextAsync(input);
            // an array is the list of items; an object is one item
            body["items"] = JsonNode.Parse(text) switch
            {
                JsonArray array => array,
                JsonObject item => new JsonArray(item),
                _ => throw new PipeCliException("--input must be a JSON object or an array of objects")
            };
        }
        if (c.Call.Get("environment") is { } environment)
        {
            body["environment"] = environment;
        }
        var wait = c.Call.Has("wait");
        if (wait)
        {
            body["wait"] = true;
            if (c.Call.Get("timeout") is { } timeout)
            {
                body["timeoutSeconds"] = int.TryParse(timeout, out var seconds) && seconds > 0
                    ? seconds
                    : throw new PipeCliException("--timeout is a number of seconds");
            }
        }

        var response = await c.Client.PostAsync($"workflows/{Escape(id)}/run", body);
        if ((int)response.Status == 429)
        {
            throw new PipeCliException($"{id} runs already and its queue is full");
        }
        Check(response);

        if (c.Call.Has("json"))
        {
            c.Output.WriteLine(Print(response.Body));
        }
        else
        {
            var status = Text(response.Body?["status"]);
            c.Output.WriteLine(wait && response.Status == System.Net.HttpStatusCode.OK
                ? $"{status} execution {Text(response.Body?["executionId"])}"
                : $"started execution {Text(response.Body?["executionId"])}");
            if (response.Body?["error"] is { } runError)
            {
                c.Error.WriteLine($"error: {runError}{(response.Body?["errorNode"] is { } node ? $" (node {node})" : "")}");
            }
            if (wait && response.Body?["items"] is JsonArray items)
            {
                c.Output.WriteLine(Print(items));
            }
        }

        return wait && Text(response.Body?["status"]) == "error" ? RunFailed : Success;
    }

    static private async Task<int> ListExecutionsAsync(Context c)
    {
        var query = new List<string>();
        foreach (var (option, parameter) in new[] { ("workflow", "workflowId"), ("status", "status"), ("limit", "limit") })
        {
            if (c.Call.Get(option) is { } value)
            {
                query.Add($"{parameter}={Uri.EscapeDataString(value)}");
            }
        }

        var response = Check(await c.Client.GetAsync("executions" + (query.Count > 0 ? "?" + string.Join('&', query) : "")));
        if (c.Call.Has("json"))
        {
            c.Output.WriteLine(Print(response.Body));
            return Success;
        }

        WriteTable(c.Output, ["ID", "WORKFLOW", "STATUS", "MODE", "STARTED"],
            (response.Body as JsonArray ?? []).Select(e => new[]
            {
                Text(e?["id"]), Text(e?["workflowName"] ?? e?["workflowId"]), Text(e?["status"]), Text(e?["mode"]), Text(e?["startedAt"])
            }));
        return Success;
    }

    static private async Task<int> ExecutionAsync(Context c, string action, bool get)
    {
        var workflowId = c.Argument(2, "a workflow id");
        var id = c.Argument(3, "an execution id");
        var path = $"executions/{Escape(workflowId)}/{Escape(id)}" + (action.Length > 0 ? "/" + action : "");

        JsonNode? body = null;
        if (action == "replay" && c.Call.Get("from") is { } from)
        {
            body = new JsonObject { ["fromNodeId"] = from };
        }

        var response = Check(get ? await c.Client.GetAsync(path) : await c.Client.PostAsync(path, body));
        c.Output.WriteLine(get || response.Body is not null ? Print(response.Body) : $"{action} {id}");
        return Success;
    }

    // ---- helpers ----

    static private PipeCliResponse Check(PipeCliResponse response)
        => response.Ok ? response : throw new PipeCliException(response.Describe());

    static private string Escape(string id) => Uri.EscapeDataString(id);

    static private string Text(JsonNode? node) => node is null ? "" : node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node.ToJsonString();

    static private string Print(JsonNode? node) => node is null ? "" : node.ToJsonString(Indented);

    static private void WriteTable(TextWriter output, string[] header, IEnumerable<string[]> rows)
    {
        var all = rows.Prepend(header).ToList();
        var widths = Enumerable.Range(0, header.Length).Select(i => all.Max(r => r[i].Length)).ToArray();
        foreach (var row in all)
        {
            output.WriteLine(string.Join("  ", row.Select((cell, i) => cell.PadRight(widths[i]))).TrimEnd());
        }
    }

    private sealed record Context(PipeCliClient Client, Call Call, TextWriter Output, TextWriter Error, TextReader Input)
    {
        public string Argument(int index, string what)
            => Call.Positionals.Count > index ? Call.Positionals[index] : throw new PipeCliException($"Missing {what}");
    }

    /// <summary>Positional arguments and <c>--option value</c> / <c>--option=value</c> / <c>--flag</c>.</summary>
    private sealed class Call
    {
        public List<string> Positionals { get; } = [];
        private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

        public bool Has(string name) => _options.ContainsKey(name);

        public string? Get(string name) => _options.GetValueOrDefault(name);

        static public Call Parse(string[] args)
        {
            var call = new Call();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                // a lone "-" is a value (stdin), not an option
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    call.Positionals.Add(arg);
                    continue;
                }

                var name = arg[2..];
                string? value = null;
                var equals = name.IndexOf('=');
                if (equals >= 0)
                {
                    (name, value) = (name[..equals], name[(equals + 1)..]);
                }
                else if (!Flags.Contains(name))
                {
                    value = i + 1 < args.Length ? args[++i] : throw new PipeCliException($"--{name} needs a value");
                }
                call._options[name] = value;
            }
            return call;
        }
    }
}

public sealed class PipeCliException : Exception
{
    public PipeCliException(string message) : base(message)
    {
    }
}
