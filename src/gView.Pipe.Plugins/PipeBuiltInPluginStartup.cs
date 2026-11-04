using gView.Pipe.Abstraction.Files;
using gView.Pipe.Abstraction.Plugins;
using gView.Pipe.Plugins.Common;
using gView.Pipe.Plugins.Common.Formats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace gView.Pipe.Plugins;

/// <summary>Registers the built-in file formats (ADR 0018); plugins add theirs the same way.</summary>
public sealed class PipeBuiltInPluginStartup : IPipePluginStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // the mailbox of the Email Trigger (ADR 0038); tests replace it
        services.TryAddSingleton<IPipeMailConnector, MailKitMailConnector>();
        // tokens of OAuth2 credentials, shared by all runs (ADR 0035)
        services.TryAddSingleton<PipeOAuth2TokenCache>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPipeFileFormat, PipeCsvFileFormat>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPipeFileFormat, PipeJsonFileFormat>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPipeFileFormat, PipeXmlFileFormat>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPipeFileFormat, PipeTextFileFormat>());
    }
}
