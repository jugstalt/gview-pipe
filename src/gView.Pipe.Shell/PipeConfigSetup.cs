namespace gView.Pipe.Shell;

/// <summary>
/// The app configuration lives in <c>_config/pipe.config</c> (not in Git); appsettings holds only ASP.NET Core
/// settings like logging. On first start the file is created from the prototype <c>_setup/proto/pipe.config</c>,
/// as in gview-gis. Paths are relative to the working directory.
/// The variable <c>PIPE_CONFIG_PATH</c> moves the <c>_config</c> folder elsewhere: the start scripts of the ZIP
/// point it next to the version folders, so the file survives updates.
/// </summary>
static public class PipeConfigSetup
{
    public const string ConfigPathVariable = "PIPE_CONFIG_PATH";
    public const string ProtoFile = "_setup/proto/pipe.config";

    static public string ConfigFile => Path.Combine(
        Environment.GetEnvironmentVariable(ConfigPathVariable) is { Length: > 0 } configPath ? configPath : "_config",
        "pipe.config");

    static public void TrySetup()
    {
        try
        {
            var config = new FileInfo(ConfigFile);
            if (config.Exists)
            {
                return;
            }

            var proto = new FileInfo(ProtoFile);
            if (!proto.Exists)
            {
                Console.WriteLine($"Warning: neither {ConfigFile} nor {ProtoFile} exists; starting with the defaults.");
                return;
            }

            Console.WriteLine("#############################################################################################################");
            Console.WriteLine("Setup:");
            Console.WriteLine($"First start: creating {ConfigFile}. You can modify this file for your production settings...");
            Console.WriteLine("#############################################################################################################");

            config.Directory!.Create();
            proto.CopyTo(config.FullName);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Warning: can't initialize the configuration for first start:");
            Console.WriteLine(ex.Message);
        }
    }

    /// <summary>Adds <see cref="ConfigFile"/> after appsettings, so environment variables and arguments still win.</summary>
    static public void AddPipeConfig(this ConfigurationManager configuration)
    {
        var sources = configuration.Sources;
        // behind appsettings.json, appsettings.{Environment}.json and user secrets
        var index = sources.Select((s, i) => (s, i)).LastOrDefault(x => x.s is Microsoft.Extensions.Configuration.Json.JsonConfigurationSource, (null!, -1)).i + 1;

        configuration.AddJsonFile(Path.GetFullPath(ConfigFile), optional: true, reloadOnChange: false);

        var added = sources[^1];
        sources.RemoveAt(sources.Count - 1);
        sources.Insert(index, added);
    }
}
