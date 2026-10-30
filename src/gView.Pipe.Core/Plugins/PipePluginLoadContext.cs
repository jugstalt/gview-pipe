using System.Reflection;
using System.Runtime.Loader;

namespace gView.Pipe.Core.Plugins;

/// <summary>
/// Isolates one plugin folder. Shared assemblies (the gView.Pipe contracts and framework packages)
/// fall through to the default context so plugin and host agree on the types, as long as the host has them.
/// A shared-prefix assembly only the plugin brings (e.g. a <c>System.*</c> package of a database driver)
/// is loaded from the plugin folder.
/// </summary>
internal sealed class PipePluginLoadContext : AssemblyLoadContext
{
    // assemblies the default context can load: framework and app folder
    static private readonly HashSet<string> HostAssemblies = ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(Path.GetFileNameWithoutExtension)
        .OfType<string>()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private readonly AssemblyDependencyResolver _resolver;
    private readonly IReadOnlyList<string> _sharedPrefixes;

    public PipePluginLoadContext(string mainAssemblyPath, IReadOnlyList<string> sharedPrefixes)
        : base($"gView.Pipe.Plugin:{Path.GetFileNameWithoutExtension(mainAssemblyPath)}", isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        _sharedPrefixes = sharedPrefixes;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (IsShared(assemblyName))
        {
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }

    private bool IsShared(AssemblyName assemblyName)
        => assemblyName.Name is { } name
           && _sharedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
           && (HostAssemblies.Contains(name) || _resolver.ResolveAssemblyToPath(assemblyName) is null);
}
