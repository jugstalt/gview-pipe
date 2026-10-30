using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;

using Serilog;

// Deploy build for gView.Pipe (same structure as gview-gis)
//
//   .\build.cmd                    => Test, Compile, CleanArtifacts, Zip, Docker
//   .\build.cmd Zip                => only the ZIP files (no docker)
//   .\build.cmd Deploy --skip Test
//   .\build.cmd Zip --platform win64
//   .\build.cmd DockerPush         => also push the image to ghcr.io
//
// Output (DeployRoot, default c:\deploy\gview-pipe):
//   download\gview-pipe-{win64|linux64}-{version}.zip
//   docker\pipe\                   docker build context
//
// ZIP structure: {version}/pipe/... (app + plugins/<Name>/) and {version}/gview-pipe.{bat|sh}.
// Same layout as gview-gis, so gView.Deploy can install it. The start scripts keep the
// runtime state in {root}/data, next to the version folders, so it survives updates.
class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Deploy);

    [Parameter("Configuration to build - Default is 'Release'")]
    readonly string Configuration = "Release";

    [Parameter("Version - Default is the <Version> from Directory.Build.props")]
    string Version;

    [Parameter("Platform to build - win64, linux64 (default: both)")]
    readonly string[] Platform = { "win64", "linux64" };

    [Parameter("Deploy root - Default is c:\\deploy\\gview-pipe")]
    readonly AbsolutePath DeployRoot = (AbsolutePath)@"c:\deploy\gview-pipe";

    [Parameter("Docker exe file name")]
    readonly string DockerExe = "docker";

    [Parameter("Docker registry for DockerPush")]
    readonly string DockerRegistry = "ghcr.io/jugstalt";

    const string AppName = "pipe";
    const string ZipName = "gview-pipe";
    const string DockerImage = "gview-pipe";

    static readonly RelativePath ShellProject = (RelativePath)"src/gView.Pipe.Shell/gView.Pipe.Shell.csproj";

    // external plugins: published into plugins/<Name>/ of the app
    static readonly RelativePath[] PluginProjects =
    {
        (RelativePath)"src/gView.Pipe.Plugins.Database/gView.Pipe.Plugins.Database.csproj",
        (RelativePath)"src/gView.Pipe.Plugins.Scripts/gView.Pipe.Plugins.Scripts.csproj",
    };

    AbsolutePath AssetsDirectory => RootDirectory / "publish";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath DownloadDirectory => DeployRoot / "download";
    AbsolutePath DockerDirectory => DeployRoot / "docker";

    AbsolutePath Artifacts(string platform) => ArtifactsDirectory / AppName / platform;

    static string RuntimeIdentifier(string platform) => platform switch
    {
        "win64" => "win-x64",
        "linux64" => "linux-x64",
        _ => throw new ArgumentException($"Unknown platform '{platform}'")
    };

    protected override void OnBuildInitialized()
    {
        if (String.IsNullOrEmpty(Version))
        {
            var buildProps = (RootDirectory / "Directory.Build.props").ReadAllText();
            var match = Regex.Match(buildProps, @"<Version>\s*([^<\s]+)\s*</Version>");
            Assert.True(match.Success, "Can't read <Version> from Directory.Build.props");
            Version = match.Groups[1].Value;
        }

        foreach (var platform in Platform)
        {
            Assert.True(platform is "win64" or "linux64", $"Unknown platform '{platform}'");
        }

        Log.Information("gView.Pipe {Version} for {Platform}", Version, String.Join(", ", Platform));
    }

    Target Test => _ => _
        .Before(Compile)
        .Executes(() =>
        {
            // Microsoft.Testing.Platform (global.json): the solution is passed with --solution
            DotNetTasks.DotNet($"test --solution {RootDirectory / "gView.Pipe.slnx"} --configuration {Configuration}");
        });

    Target Compile => _ => _
        .DependsOn(Test)
        .Executes(() =>
        {
            foreach (var platform in Platform)
            {
                var artifacts = Artifacts(platform);
                var runtime = RuntimeIdentifier(platform);

                Log.Information("Compile {App} {Platform}", AppName, platform);

                artifacts.CreateOrCleanDirectory();
                // Properties/PublishProfiles/{win64|linux64}.pubxml (runtime, framework dependent)
                DotNetTasks.DotNetPublish(s => s
                    .SetProject(RootDirectory / ShellProject)
                    .SetConfiguration(Configuration)
                    .SetProperty("PublishProfile", platform)
                    .SetProperty("PublishDir", artifacts)
                    .SetVersion(Version));

                foreach (var pluginProject in PluginProjects)
                {
                    var pluginName = Path.GetFileNameWithoutExtension(pluginProject);

                    Log.Information("Compile plugin {Plugin} {Platform}", pluginName, platform);

                    // per RID, so the native libraries (SQLite, SqlClient) match the platform
                    DotNetTasks.DotNetPublish(s => s
                        .SetProject(RootDirectory / pluginProject)
                        .SetConfiguration(Configuration)
                        .SetRuntime(runtime)
                        .SetSelfContained(false)
                        .SetVersion(Version)
                        .SetProperty("InstallPipePlugin", "false")
                        .SetOutput(artifacts / "plugins" / pluginName));
                }
            }
        });

    Target CleanArtifacts => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            foreach (var platform in Platform)
            {
                var artifacts = Artifacts(platform);

                // never deploy runtime state or developer settings
                foreach (var directory in new[] { "data", "files", "scripts" })
                {
                    (artifacts / directory).DeleteDirectory();
                }
                foreach (var file in artifacts.GlobFiles("appsettings.Development.json", "*.user"))
                {
                    file.DeleteFile();
                }

                // third party pdb files and doc xml files
                foreach (var file in artifacts.GlobFiles("**/*.pdb", "**/*.xml"))
                {
                    if (!file.Name.StartsWith("gView.", StringComparison.OrdinalIgnoreCase))
                    {
                        file.DeleteFile();
                    }
                }
            }
        });

    Target Zip => _ => _
        .DependsOn(CleanArtifacts)
        .Executes(() =>
        {
            DownloadDirectory.CreateDirectory();

            // the ZIP files are independent => create them in parallel
            Parallel.ForEach(Platform, platform =>
            {
                var zipFile = DownloadDirectory / $"{ZipName}-{platform}-{Version}.zip";

                // ZIP content as entry path => source file (no staging copy)
                var content = new ZipContent();
                var appPrefix = $"{Version}/{AppName}/";

                content.AddDirectory(Artifacts(platform), appPrefix, overwrite: true);
                content.AddDirectory(AssetsDirectory / AppName / "override", appPrefix, overwrite: true);
                content.AddDirectory(AssetsDirectory / AppName / platform / "override", appPrefix, overwrite: true);

                // start scripts
                foreach (var script in (AssetsDirectory / AppName / platform).GlobFiles("*.bat", "*.sh"))
                {
                    content.AddFile(script, $"{Version}/{script.Name}", overwrite: true);
                }

                var stopwatch = Stopwatch.StartNew();
                zipFile.DeleteFile();
                content.WriteZip(zipFile, $"{Version}/");

                Log.Information("Zip {ZipFile} ({Seconds:0.0}s)", zipFile, stopwatch.Elapsed.TotalSeconds);
            });
        });

    Target Docker => _ => _
        .DependsOn(CleanArtifacts)
        .After(Zip)
        .OnlyWhenStatic(() => Platform.Contains("linux64"))
        .Executes(() =>
        {
            var contextDirectory = DockerDirectory / AppName;

            contextDirectory.CreateOrCleanDirectory();

            CopyDirectory(Artifacts("linux64"), contextDirectory / "app", overwrite: true);
            CopyDirectory(AssetsDirectory / AppName / "override", contextDirectory / "app", overwrite: true);
            CopyDirectory(AssetsDirectory / AppName / "docker" / "override", contextDirectory / "app", overwrite: true);
            (AssetsDirectory / AppName / "docker" / "Dockerfile").Copy(contextDirectory / "Dockerfile", ExistsPolicy.FileOverwrite);

            BuildDockerImage(DockerImage, contextDirectory);
        });

    Target DockerPush => _ => _
        .DependsOn(Docker)
        .Executes(() =>
        {
            RunDocker($"push {DockerRegistry}/{DockerImage}:{Version}");
            RunDocker($"push {DockerRegistry}/{DockerImage}:latest");
        });

    Target Deploy => _ => _
        .DependsOn(Zip, Docker);

    void BuildDockerImage(string image, AbsolutePath contextDirectory, string buildArguments = "")
    {
        Log.Information("Build docker image {Image}:{Version}", image, Version);

        RunDocker($"build -t {image}:{Version} {buildArguments} -f Dockerfile .", contextDirectory);
        RunDocker($"tag {image}:{Version} {image}:latest");
        RunDocker($"tag {image}:{Version} {DockerRegistry}/{image}:{Version}");
        RunDocker($"tag {image}:{Version} {DockerRegistry}/{image}:latest");
    }

    void RunDocker(string arguments, AbsolutePath workingDirectory = null)
    {
        ProcessTasks.StartProcess(DockerExe, arguments,
                workingDirectory: workingDirectory,
                logger: (_, text) => Log.Information(text))
            .AssertZeroExitCode();
    }

    static void CopyDirectory(AbsolutePath source, AbsolutePath target, bool overwrite)
    {
        if (!source.DirectoryExists())
        {
            return;
        }

        target.CreateDirectory();

        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var targetFile = Path.Combine(target, Path.GetRelativePath(source, file));
            if (overwrite || !File.Exists(targetFile))
            {
                File.Copy(file, targetFile, overwrite);
            }
        }
    }
}
