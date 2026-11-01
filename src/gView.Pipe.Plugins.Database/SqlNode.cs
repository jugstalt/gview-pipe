using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Database;

public enum SqlProvider
{
    /// <summary>The provider named in the connection string credential.</summary>
    FromCredential,
    SqlServer,
    PostgreSql,
    Sqlite
}

public enum SqlOperation
{
    /// <summary>Every result row becomes an item.</summary>
    Query,

    /// <summary>INSERT, UPDATE, DELETE, …: one item with the number of affected rows.</summary>
    Execute
}

public sealed class SqlNodeParameters
{
    /// <summary>Id of the built-in Connection String credential type.</summary>
    public const string ConnectionStringCredential = "d4e5f6a7-b8c9-4d0e-9f1a-2b3c4d5e6f14";

    [Required, PipeCredential(ConnectionStringCredential)]
    [PipeParameter(Label = "Connection", Order = 1, Expressions = false, Description = "Connection String credential: provider (sqlserver, postgres or sqlite) and connection string")]
    public string? CredentialId { get; set; }

    [PipeParameter(Label = "Database", Order = 2, Expressions = false,
        Description = "From credential: its provider field (sqlserver, postgres or sqlite)")]
    public SqlProvider Provider { get; set; } = SqlProvider.FromCredential;

    [PipeParameter(Label = "Operation", Order = 3, Expressions = false)]
    public SqlOperation Operation { get; set; } = SqlOperation.Query;

    [Required, PipeParameter(Label = "SQL", Order = 4, Code = true, CodeLanguage = "sql",
        Description = "Use @name for values; they are passed as parameters, never inserted into the SQL text")]
    public string Sql { get; set; } = "";

    [PipeParameter(Label = "Parameters", Order = 5, Description = "@name → value or expression, evaluated per item")]
    public Dictionary<string, JsonNode?> Parameters { get; set; } = new();

    [PipeParameter(Label = "Timeout (seconds)", Order = 6, Expressions = false)]
    [Range(0, 3600)]
    public int CommandTimeout { get; set; } = 30;
}

/// <summary>Fields of the Connection String credential the node reads.</summary>
internal sealed class ConnectionStringData
{
    public string? Provider { get; set; }
    public string ConnectionString { get; set; } = "";
}

[PipeNode("98f2efaf-ce68-4446-9997-46bbca748494", "SQL",
    Category = "Database",
    Description = "Runs SQL against SQL Server, PostgreSQL or SQLite, once per input item. "
                  + "Query returns the rows as items, Execute the number of affected rows.")]
public sealed class SqlNode : IPipeNode<SqlNodeParameters>
{
    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput();
        // the SQL text is never evaluated: values only travel as parameters
        var sql = context.RawParameters["sql"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new PipeException("The SQL statement is empty");
        }

        var first = context.GetParameters<SqlNodeParameters>(items.Count > 0 ? items[0] : null);
        if (string.IsNullOrEmpty(first.CredentialId))
        {
            throw new PipeException("Select a connection");
        }
        var credential = await context.GetCredentialAsync<ConnectionStringData>(first.CredentialId, cancellationToken);

        await using var connection = Factory(first.Provider, credential).CreateConnection()!;
        connection.ConnectionString = credential.ConnectionString;
        await connection.OpenAsync(cancellationToken);

        var output = new List<PipeItem>();
        for (var i = 0; i < items.Count; i++)
        {
            var parameters = i == 0 ? first : context.GetParameters<SqlNodeParameters>(items[i], i);

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = parameters.CommandTimeout;
            foreach (var (name, value) in parameters.Parameters.Where(p => p.Key.Trim().Length > 0))
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name.Trim().TrimStart('@', ':', '$');
                parameter.Value = ToDbValue(value);
                command.Parameters.Add(parameter);
            }

            if (parameters.Operation == SqlOperation.Execute)
            {
                var affected = await command.ExecuteNonQueryAsync(cancellationToken);
                context.Logger.LogInformation("{Rows} row(s) affected", affected);
                output.Add(new PipeItem(new JsonObject { ["affectedRows"] = affected }));
            }
            else
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                var before = output.Count;
                while (await reader.ReadAsync(cancellationToken))
                {
                    output.Add(new PipeItem(ToJson(reader)));
                }
                context.Logger.LogInformation("{Rows} row(s) read", output.Count - before);
            }
        }

        return PipeNodeResult.FromMain(output);
    }

    static private DbProviderFactory Factory(SqlProvider provider, ConnectionStringData credential)
    {
        if (provider == SqlProvider.FromCredential)
        {
            provider = credential.Provider?.Trim().ToLowerInvariant() switch
            {
                "sqlserver" or "mssql" or "sql server" => SqlProvider.SqlServer,
                "postgres" or "postgresql" or "npgsql" or "pg" => SqlProvider.PostgreSql,
                "sqlite" => SqlProvider.Sqlite,
                var other => throw new PipeException(string.IsNullOrEmpty(other)
                    ? "The connection has no provider; set it in the credential (sqlserver, postgres, sqlite) or choose the database here"
                    : $"Unknown provider '{credential.Provider}'; use sqlserver, postgres or sqlite")
            };
        }

        return provider switch
        {
            SqlProvider.SqlServer => SqlClientFactory.Instance,
            SqlProvider.PostgreSql => NpgsqlFactory.Instance,
            _ => SqliteFactory.Instance
        };
    }

    /// <summary>JSON values to parameter values; objects and arrays are passed as JSON text.</summary>
    static private object ToDbValue(JsonNode? value) => value switch
    {
        null => DBNull.Value,
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => DBNull.Value,
            JsonValueKind.Number when v.TryGetValue<long>(out var integer) => integer,
            JsonValueKind.Number when v.TryGetValue<decimal>(out var number) => number,
            JsonValueKind.Number => double.Parse(v.ToJsonString(), CultureInfo.InvariantCulture),
            _ => v.ToJsonString()
        },
        _ => value.ToJsonString()
    };

    /// <summary>A result row as JSON: dates as ISO text, binary data as base64, unknown types as text.</summary>
    static private JsonObject ToJson(DbDataReader reader)
    {
        var json = new JsonObject();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            if (string.IsNullOrEmpty(name))
            {
                name = $"column{i + 1}";
            }

            json[name] = reader.IsDBNull(i) ? null : reader.GetValue(i) switch
            {
                string s => s,
                bool b => b,
                byte or short or int or long => JsonValue.Create(Convert.ToInt64(reader.GetValue(i))),
                decimal d => d,
                float or double => JsonValue.Create(Convert.ToDouble(reader.GetValue(i))),
                DateTime dt => dt.ToString("O"),
                DateTimeOffset dto => dto.ToString("O"),
                DateOnly date => date.ToString("O"),
                TimeOnly time => time.ToString("O"),
                TimeSpan span => span.ToString("c"),
                Guid guid => guid.ToString(),
                byte[] bytes => Convert.ToBase64String(bytes),
                var other => Convert.ToString(other, CultureInfo.InvariantCulture)
            };
        }
        return json;
    }
}
