using DuckDB.EFCoreProvider.Infrastructure.Internal;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     Creates a disposable local catalog skeleton so Quack can bind remote default expressions while replacing it
///     with the stateful remote catalog.
/// </summary>
/// <remarks>
///     Quack 1.5.x binds table definitions before the new catalog alias is visible. Function defaults therefore need
///     the alias to exist, and sequence defaults additionally need matching placeholder sequence names. The skeleton
///     never stores application data and is atomically replaced by the Quack attachment.
/// </remarks>
internal static class QuackCatalogBootstrapper
{
    private const string SequenceDiscoverySql =
        "SELECT schema_name, sequence_name "
        + "FROM quack_query($endpoint, "
        + "'SELECT schema_name, sequence_name FROM duckdb_sequences() ORDER BY schema_name, sequence_name', "
        + "token := $token, disable_ssl := $disable_ssl);";

    internal static void Prepare(DbConnection connection, QuackOptions options)
        => CreateSkeleton(connection, options.CatalogName, DiscoverSequences(connection, options));

    internal static async Task PrepareAsync(
        DbConnection connection,
        QuackOptions options,
        CancellationToken cancellationToken)
        => await CreateSkeletonAsync(
                connection,
                options.CatalogName,
                await DiscoverSequencesAsync(connection, options, cancellationToken).ConfigureAwait(false),
                cancellationToken)
            .ConfigureAwait(false);

    private static IReadOnlyList<RemoteSequence> DiscoverSequences(
        DbConnection connection,
        QuackOptions options)
    {
        using var command = CreateSequenceDiscoveryCommand(connection, options);
        using var reader = command.ExecuteReader();
        var sequences = new List<RemoteSequence>();
        while (reader.Read())
        {
            sequences.Add(new RemoteSequence(reader.GetString(0), reader.GetString(1)));
        }

        return sequences;
    }

    private static async Task<IReadOnlyList<RemoteSequence>> DiscoverSequencesAsync(
        DbConnection connection,
        QuackOptions options,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSequenceDiscoveryCommand(connection, options);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var sequences = new List<RemoteSequence>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sequences.Add(new RemoteSequence(reader.GetString(0), reader.GetString(1)));
        }

        return sequences;
    }

    private static DbCommand CreateSequenceDiscoveryCommand(
        DbConnection connection,
        QuackOptions options)
    {
        var command = connection.CreateCommand();
        command.CommandText = SequenceDiscoverySql;
        AddParameter(command, "endpoint", options.Endpoint);
        AddParameter(command, "token", options.Token);
        AddParameter(command, "disable_ssl", options.DisableSsl);
        return command;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static void CreateSkeleton(
        DbConnection connection,
        string catalogName,
        IReadOnlyList<RemoteSequence> sequences)
    {
        Execute(connection, $"ATTACH ':memory:' AS {QuackSqlTextBuilder.QuoteIdentifier(catalogName)};");
        foreach (var sequence in sequences.Distinct())
        {
            EnsureSchema(connection, catalogName, sequence.SchemaName);
            Execute(connection, BuildCreateSequenceSql(catalogName, sequence));
        }
    }

    private static async Task CreateSkeletonAsync(
        DbConnection connection,
        string catalogName,
        IReadOnlyList<RemoteSequence> sequences,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
                connection,
                $"ATTACH ':memory:' AS {QuackSqlTextBuilder.QuoteIdentifier(catalogName)};",
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var sequence in sequences.Distinct())
        {
            await EnsureSchemaAsync(connection, catalogName, sequence.SchemaName, cancellationToken)
                .ConfigureAwait(false);
            await ExecuteAsync(connection, BuildCreateSequenceSql(catalogName, sequence), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static void EnsureSchema(DbConnection connection, string catalogName, string schemaName)
    {
        if (!string.Equals(schemaName, "main", StringComparison.OrdinalIgnoreCase))
        {
            Execute(connection, BuildCreateSchemaSql(catalogName, schemaName));
        }
    }

    private static Task EnsureSchemaAsync(
        DbConnection connection,
        string catalogName,
        string schemaName,
        CancellationToken cancellationToken)
        => string.Equals(schemaName, "main", StringComparison.OrdinalIgnoreCase)
            ? Task.CompletedTask
            : ExecuteAsync(connection, BuildCreateSchemaSql(catalogName, schemaName), cancellationToken);

    private static string BuildCreateSchemaSql(string catalogName, string schemaName)
        => $"CREATE SCHEMA IF NOT EXISTS {Qualify(catalogName, schemaName)};";

    private static string BuildCreateSequenceSql(string catalogName, RemoteSequence sequence)
        => $"CREATE SEQUENCE {Qualify(catalogName, sequence.SchemaName, sequence.SequenceName)};";

    private static string Qualify(params string[] identifiers)
        => string.Join('.', identifiers.Select(QuackSqlTextBuilder.QuoteIdentifier));

    private static void Execute(DbConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static async Task ExecuteAsync(
        DbConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record RemoteSequence(string SchemaName, string SequenceName);
}