using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Plugins;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Core.Options;
using System.Reflection;
using System.Runtime.Loader;

namespace gView.Pipe.Core.Plugins;

/// <summary>
/// Everything found in the plugin assemblies. Built once while services are configured,
/// because plugin startups have to register their services before the container is built.
/// </summary>
public sealed class PipePluginCatalog
{
    private PipePluginCatalog(
        IReadOnlyList<Assembly> assemblies,
        IReadOnlyList<PipeNodeDescriptor> nodes,
        IReadOnlyList<PipeCredentialTypeDescriptor> credentialTypes,
        IReadOnlyList<IPipePluginStartup> startups,
        IReadOnlyList<string> warnings)
    {
        Assemblies = assemblies;
        Nodes = nodes;
        CredentialTypes = credentialTypes;
        Startups = startups;
        Warnings = warnings;
    }

    public IReadOnlyList<Assembly> Assemblies { get; }
    public IReadOnlyList<PipeNodeDescriptor> Nodes { get; }
    public IReadOnlyList<PipeCredentialTypeDescriptor> CredentialTypes { get; }
    public IReadOnlyList<IPipePluginStartup> Startups { get; }

    /// <summary>Load problems; logged by the registry once logging is available.</summary>
    public IReadOnlyList<string> Warnings { get; }

    static public PipePluginCatalog Load(PipeCoreOptions options)
    {
        var warnings = new List<string>();
        var assemblies = new List<Assembly>();

        void Add(Assembly assembly)
        {
            if (!assemblies.Contains(assembly))
            {
                assemblies.Add(assembly);
            }
        }

        options.PluginAssemblies.ForEach(Add);

        if (options.ScanApplicationDirectory)
        {
            foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, options.ApplicationPluginPattern))
            {
                try
                {
                    Add(AssemblyLoadContext.Default.LoadFromAssemblyName(AssemblyName.GetAssemblyName(file)));
                }
                catch (Exception ex)
                {
                    warnings.Add($"Can't load plugin assembly {file}: {ex.Message}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(options.PluginPath) && Directory.Exists(options.PluginPath))
        {
            foreach (var folder in Directory.GetDirectories(Path.GetFullPath(options.PluginPath)))
            {
                var mainAssembly = Path.Combine(folder, $"{Path.GetFileName(folder)}.dll");
                if (!File.Exists(mainAssembly))
                {
                    warnings.Add($"Plugin folder {folder} contains no {Path.GetFileName(mainAssembly)}; skipped");
                    continue;
                }

                try
                {
                    var context = new PipePluginLoadContext(mainAssembly, options.SharedAssemblyPrefixes);
                    Add(context.LoadFromAssemblyPath(mainAssembly));
                }
                catch (Exception ex)
                {
                    warnings.Add($"Can't load plugin {mainAssembly}: {ex.Message}");
                }
            }
        }

        var nodes = new Dictionary<Guid, PipeNodeDescriptor>();
        var credentialTypes = new Dictionary<Guid, PipeCredentialTypeDescriptor>();
        var startups = new List<IPipePluginStartup>();

        foreach (var assembly in assemblies)
        {
            var source = assembly.GetName().Name ?? assembly.FullName ?? "unknown";

            foreach (var type in GetLoadableTypes(assembly, warnings))
            {
                if (!type.IsClass || type.IsAbstract)
                {
                    continue;
                }

                if (type.GetCustomAttribute<PipeNodeAttribute>() is { } nodeAttribute)
                {
                    if (!typeof(IPipeNode).IsAssignableFrom(type))
                    {
                        warnings.Add($"{type.FullName} has [PipeNode] but does not implement IPipeNode; skipped");
                    }
                    else if (nodes.TryGetValue(nodeAttribute.Id, out var existing))
                    {
                        warnings.Add($"Node id {nodeAttribute.Id} of {type.FullName} is already used by {existing.NodeType.FullName}; skipped");
                    }
                    else
                    {
                        nodes[nodeAttribute.Id] = CreateNodeDescriptor(type, nodeAttribute, source);
                    }
                }

                if (type.GetCustomAttribute<PipeCredentialTypeAttribute>() is { } credentialAttribute)
                {
                    if (credentialTypes.TryGetValue(credentialAttribute.Id, out var existing))
                    {
                        warnings.Add($"Credential type id {credentialAttribute.Id} of {type.FullName} is already used by {existing.DataType.FullName}; skipped");
                    }
                    else
                    {
                        credentialTypes[credentialAttribute.Id] = new PipeCredentialTypeDescriptor
                        {
                            Id = credentialAttribute.Id,
                            Name = credentialAttribute.Name,
                            Description = credentialAttribute.Description,
                            DataType = type,
                            Source = source
                        };
                    }
                }

                if (typeof(IPipePluginStartup).IsAssignableFrom(type))
                {
                    try
                    {
                        startups.Add((IPipePluginStartup)Activator.CreateInstance(type)!);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"Can't create plugin startup {type.FullName}: {ex.Message}");
                    }
                }
            }
        }

        return new PipePluginCatalog(assemblies, nodes.Values.ToList(), credentialTypes.Values.ToList(), startups, warnings);
    }

    static private PipeNodeDescriptor CreateNodeDescriptor(Type type, PipeNodeAttribute attribute, string source)
    {
        var isTrigger = typeof(IPipeTriggerNode).IsAssignableFrom(type);
        var parametersType = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipeNode<>))
            ?.GetGenericArguments()[0];

        return new PipeNodeDescriptor
        {
            Id = attribute.Id,
            Name = attribute.Name,
            Description = attribute.Description,
            Category = attribute.Category,
            Version = attribute.Version,
            NodeType = type,
            ParametersType = parametersType,
            IsTrigger = isTrigger,
            HasDynamicPorts = typeof(IPipeDynamicPorts).IsAssignableFrom(type),
            Obsolete = attribute.Obsolete,
            Inputs = isTrigger ? [] : attribute.Inputs.Select(p => new PipePort(p)).ToList(),
            Outputs = attribute.Outputs.Select(p => new PipePort(p)).ToList(),
            Source = source
        };
    }

    static private IEnumerable<Type> GetLoadableTypes(Assembly assembly, List<string> warnings)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            warnings.Add($"Some types of {assembly.GetName().Name} could not be loaded: {ex.LoaderExceptions.FirstOrDefault()?.Message}");
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
