using gView.Pipe.Abstraction.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace gView.Pipe.Plugins.Sftp;

/// <summary>Registers the connector of the SFTP node (ADR 0037); tests replace it.</summary>
public sealed class SftpPluginStartup : IPipePluginStartup
{
    public void ConfigureServices(IServiceCollection services)
        => services.TryAddSingleton<ISftpConnector, SshNetSftpConnector>();
}
