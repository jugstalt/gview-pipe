# Plugins

Built-in nodes and external nodes use the same mechanism: assemblies are scanned for `[PipeNode]` and `[PipeCredentialType]` classes.

## An external plugin project

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>

  <ItemGroup>
    <!-- the contracts come from the host: never ship them with the plugin -->
    <PackageReference Include="gView.Pipe.Abstraction" Version="…" ExcludeAssets="runtime" />
  </ItemGroup>

</Project>
```

`gView.Pipe.Plugins.Scripts` and `gView.Pipe.Plugins.Database` in the repository are complete examples. They use a project reference with `Private="false" ExcludeAssets="runtime"`.

Build, then copy the output into `plugins/<Name>/` of the app, where `<Name>` is the name of the main assembly:

```
plugins\
  MyCompany.PipeNodes\
    MyCompany.PipeNodes.dll
    (dependencies, native libraries)
```

## How plugins are loaded

- The app folder is scanned for `gView.Pipe.Plugins*.dll` (the built-in nodes). `Pipe:PluginPath` is scanned for plugin folders.
- Each plugin folder is loaded into its **own `AssemblyLoadContext`**, with its dependencies resolved from its `deps.json`. Plugins may use different versions of a library without conflicts.
- Assemblies named `gView.Pipe.Abstraction`, `Microsoft.Extensions.*`, `Microsoft.AspNetCore.*` and `System.*` always come from the host, so the plugin and the host share the same types. Don't ship them.
- Plugins are loaded when the host configures its services, so they can register services before the container is built. Plugins can't be unloaded: restart the app after an update.
- A problem while loading gives a warning in the log that starts with `Plugins:`, and the plugin is skipped. An id used by two classes keeps the first and warns.

Workflows that use a node of a plugin that isn't installed still load, and the node's data is kept. Running such a node fails with "Unknown node type".

## Services and options

Implement `IPipePluginStartup` (public, parameterless constructor) to register services:

```csharp
public sealed class MyPluginStartup : IPipePluginStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions<MyOptions>().Configure<IServiceProvider>((options, provider) =>
        {
            if (provider.GetService<IConfiguration>() is { } configuration)
            {
                configuration.GetSection("Pipe:MyPlugin").Bind(options);
            }
        });
        services.AddHttpClient("my-plugin");
    }
}
```

Nodes get services through their constructor. Settings an administrator controls (released folders, servers) belong in options like this, never in a node parameter: that way a workflow can't widen what it is allowed to do.

## Credential types

A credential type is a POCO with `[PipeCredentialType]`; its properties are the fields of the form:

```csharp
[PipeCredentialType("5b7f3c2a-1d4e-4f7a-9c0b-2a6d8e1f3b45", "My Service", Description = "Token and URL of My Service")]
public sealed class MyServiceCredential
{
    [Required, PipeParameter(Label = "Base URL")]
    public string Url { get; set; } = "";

    [Required, Secret, PipeParameter(Label = "Token")]
    public string Token { get; set; } = "";
}
```

A node references it with `[PipeCredential("5b7f3c2a-…")]` on a `string?` parameter and reads it with `await context.GetCredentialAsync<MyServiceCredential>(parameters.CredentialId)`. Never log secret values and never copy them into items: items end up in the execution history.

## Rules for ids

- The GUID of a node or credential type is its identity in stored workflows. **Never change it**, and don't reuse it.
- Built-in ids are listed in `PipeBuiltIn.cs` of `gView.Pipe.Plugins`.
- When a node's parameters change incompatibly, increase `Version` and add an `IPipeNodeMigration` instead of a new id.
