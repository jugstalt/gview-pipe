using gView.Pipe.Core.Options;
using gView.Pipe.Core.Plugins;
using gView.Pipe.Docs;

// dotnet run --project src/gView.Pipe.Docs [-- <output folder>]
// Writes the node reference (one page per category, an index and the credential types) as Markdown.
var output = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(FindRepositoryRoot(), "docs", "site", "en", "user-guide", "nodes");

var catalog = PipePluginCatalog.Load(new PipeCoreOptions { PluginPath = null });
foreach (var warning in catalog.Warnings)
{
    Console.Error.WriteLine($"warning: {warning}");
}

if (catalog.Nodes.Count == 0)
{
    Console.Error.WriteLine("error: no nodes found");
    return 1;
}

var files = NodeReferenceWriter.Write(catalog, output);
Console.WriteLine($"{catalog.Nodes.Count} nodes, {catalog.CredentialTypes.Count} credential types: {files} pages in {output}");
return 0;

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "gView.Pipe.slnx")))
        {
            return directory.FullName;
        }
    }

    throw new InvalidOperationException("gView.Pipe.slnx not found; pass the output folder as an argument");
}
