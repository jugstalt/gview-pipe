using System.Text.Json.Nodes;

namespace gView.Pipe.Abstraction.Nodes;

/// <summary>
/// Implemented by nodes that changed their parameters. Called before execution when the
/// workflow stores an older <c>typeVersion</c> than <see cref="PipeNodeAttribute.Version"/>.
/// </summary>
public interface IPipeNodeMigration
{
    JsonObject MigrateParameters(int fromVersion, JsonObject parameters);
}
