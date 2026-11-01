namespace gView.Pipe.Shell;

/// <summary>
/// The app configuration lives in <c>_config</c> (not in Git): <c>pipe.config</c> for the app and <c>auth.config</c>
/// for the login (ADR 0014); appsettings holds only ASP.NET Core settings like logging. On first start each file is
/// created from its prototype in <c>_setup/proto</c>, as in gview-gis. Paths are relative to the working directory.
/// The variable <c>PIPE_CONFIG_PATH</c> moves the <c>_config</c> folder elsewhere: the start scripts of the ZIP
/// point it next to the version folders, so the files survive updates.
/// </summary>
static public class PipeConfigSetup
{
    public const string ConfigPathVariable = "PIPE_CONFIG_PATH";
    public const string ProtoPath = "_setup/proto";

    /// <summary>The files in their load order; a later file wins.</summary>
    static public readonly string[] ConfigFileNames = ["pipe.config", "auth.config"];

    static public string ConfigPath =>
        Environment.GetEnvironmentVariable(ConfigPathVariable) is { Length: > 0 } configPath ? configPath : "_config";

    static public void TrySetup()
    {
        foreach (var name in ConfigFileNames)
        {
            TrySetup(Path.Combine(ConfigPath, name), Path.Combine(ProtoPath, name));
        }
    }

    static private void TrySetup(string configFile, string protoFile)
    {
        try
        {
            var config = new FileInfo(configFile);
            if (config.Exists)
            {
                return;
            }

            var proto = new FileInfo(protoFile);
            if (!proto.Exists)
            {
                Console.WriteLine($"Warning: neither {configFile} nor {protoFile} exists; starting with the defaults.");
                return;
            }

            Console.WriteLine("#############################################################################################################");
            Console.WriteLine("Setup:");
            Console.WriteLine($"First start: creating {configFile}. You can modify this file for your production settings...");
            Console.WriteLine("#############################################################################################################");

            config.Directory!.Create();
            proto.CopyTo(config.FullName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: can't initialize {configFile} for first start:");
            Console.WriteLine(ex.Message);
        }
    }

    /// <summary>Adds the files of <see cref="ConfigPath"/> after appsettings, so environment variables and arguments still win.</summary>
    static public void AddPipeConfig(this ConfigurationManager configuration)
    {
        var sources = configuration.Sources;
        // behind appsettings.json, appsettings.{Environment}.json and user secrets
        var index = sources.Select((s, i) => (s, i)).LastOrDefault(x => x.s is Microsoft.Extensions.Configuration.Json.JsonConfigurationSource, (null!, -1)).i + 1;

        foreach (var name in ConfigFileNames)
        {
            configuration.AddJsonFile(Path.GetFullPath(Path.Combine(ConfigPath, name)), optional: true, reloadOnChange: false);

            var added = sources[^1];
            sources.RemoveAt(sources.Count - 1);
            sources.Insert(index++, added);
        }
    }
}
