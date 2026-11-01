using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Expressions;
using gView.Pipe.Abstraction.Files;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Abstraction.Users;
using gView.Pipe.Core.Engine;
using gView.Pipe.Core.Expressions;
using gView.Pipe.Core.Files;
using gView.Pipe.Core.Options;
using gView.Pipe.Core.Plugins;
using gView.Pipe.Core.Storage;
using gView.Pipe.Core.Triggers;
using gView.Pipe.Core.Users;
using gView.Pipe.Core.Variables;
using gView.Pipe.Abstraction.Variables;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace gView.Pipe.Core.Extensions.DependencyInjection;

static public class ServicesExtensions
{
    /// <summary>
    /// Adds the engine, plugin loading, scheduler and trigger management.
    /// Plugins are loaded right here, so their startups can register services.
    /// Without <see cref="AddPipeFileStore"/> everything is kept in memory.
    /// </summary>
    static public IServiceCollection AddPipeCore(this IServiceCollection services, Action<PipeCoreOptions>? configure = null)
    {
        var options = new PipeCoreOptions();
        configure?.Invoke(options);

        var catalog = PipePluginCatalog.Load(options);
        foreach (var startup in catalog.Startups)
        {
            startup.ConfigureServices(services);
        }

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
        services.AddSingleton(catalog);
        services.AddLogging();
        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<IPipeNodeRegistry, PipeNodeRegistry>();
        services.TryAddSingleton<IPipeCredentialTypeRegistry, PipeCredentialTypeRegistry>();
        services.TryAddSingleton<PipeJavaScriptEngine>();
        services.TryAddSingleton<IPipeExpressionEvaluator>(sp => sp.GetRequiredService<PipeJavaScriptEngine>());
        services.TryAddSingleton<IPipeScriptRunner>(sp => sp.GetRequiredService<PipeJavaScriptEngine>());
        services.TryAddSingleton<IPipeEngine, PipeEngine>();
        services.TryAddSingleton<IPipeWorkflowRunner, PipeWorkflowRunner>();
        // observes every execution, so live views see runs of all triggers
        services.TryAddSingleton<PipeExecutionMonitor>();
        services.TryAddSingleton<IPipeRunningExecutions>(sp => sp.GetRequiredService<PipeExecutionMonitor>());
        services.AddSingleton<IPipeExecutionObserver>(sp => sp.GetRequiredService<PipeExecutionMonitor>());
        services.TryAddSingleton<PipeScheduler>();
        services.TryAddSingleton<PipeWebhookRegistry>();
        services.TryAddSingleton<IPipeWebhookRouter>(sp => sp.GetRequiredService<PipeWebhookRegistry>());
        services.TryAddSingleton<IPipeTriggerHost, PipeTriggerHost>();
        services.TryAddSingleton<IPipeUserContext, PipeAnonymousUserContext>();
        services.TryAddSingleton<IPipeFileRoots, PipeFileRoots>();

        services.TryAddSingleton<PipeTriggerManager>();
        services.TryAddSingleton<IPipeTriggerManager>(sp => sp.GetRequiredService<PipeTriggerManager>());
        services.AddHostedService(sp => sp.GetRequiredService<PipeTriggerManager>());

        services.TryAddSingleton<IPipeWorkflowStore, PipeMemoryWorkflowStore>();
        services.TryAddSingleton<IPipeWorkflowGroupStore, PipeMemoryWorkflowGroupStore>();
        services.TryAddSingleton<IPipeExecutionStore, PipeMemoryExecutionStore>();
        services.TryAddSingleton<IPipeCredentialStore, PipeMemoryCredentialStore>();
        services.TryAddSingleton<IPipeVariableStore, PipeMemoryVariableStore>();
        services.TryAddSingleton<IPipeEnvironments, PipeEnvironmentService>();

        return services;
    }

    /// <summary>
    /// Stores workflows, executions and (encrypted) credentials as JSON files below <see cref="PipeFileStoreOptions.RootPath"/>.
    /// </summary>
    static public IServiceCollection AddPipeFileStore(this IServiceCollection services, Action<PipeFileStoreOptions>? configure = null)
    {
        var options = new PipeFileStoreOptions();
        configure?.Invoke(options);

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));

        var dataProtection = services.AddDataProtection();
        if (options.PersistDataProtectionKeys)
        {
            dataProtection
                .SetApplicationName("gView.Pipe")
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetFullPath(options.RootPath), "keys")));
        }

        services.Replace(ServiceDescriptor.Singleton<IPipeWorkflowStore, PipeFileWorkflowStore>());
        services.Replace(ServiceDescriptor.Singleton<IPipeWorkflowGroupStore, PipeFileWorkflowGroupStore>());
        services.Replace(ServiceDescriptor.Singleton<IPipeExecutionStore, PipeFileExecutionStore>());
        services.Replace(ServiceDescriptor.Singleton<IPipeCredentialStore, PipeFileCredentialStore>());
        services.Replace(ServiceDescriptor.Singleton<IPipeVariableStore, PipeFileVariableStore>());
        services.TryAddSingleton<PipeSecretValues>();

        return services;
    }
}
