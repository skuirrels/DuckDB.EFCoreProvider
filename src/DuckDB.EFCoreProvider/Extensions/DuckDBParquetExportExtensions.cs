using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>Compression codecs supported by DuckDB's Parquet writer.</summary>
public enum DuckDBParquetCompression
{
    /// <summary>DuckDB's default Snappy compression.</summary>
    Snappy,
    /// <summary>Zstandard compression.</summary>
    Zstd,
    /// <summary>Gzip compression.</summary>
    Gzip,
    /// <summary>Uncompressed Parquet output.</summary>
    Uncompressed
}

/// <summary>Options for exporting a translated EF Core query to Parquet.</summary>
/// <typeparam name="T">The query result type.</typeparam>
public sealed class DuckDBParquetExportOptions<T>
{
    private readonly List<MemberInfo> _partitionMembers = [];

    internal IReadOnlyList<MemberInfo> PartitionMembers => _partitionMembers;
    internal bool OverwriteOrIgnoreEnabled { get; private set; }
    internal DuckDBParquetCompression CompressionCodec { get; private set; } = DuckDBParquetCompression.Snappy;

    /// <summary>Partitions output by one or more projected properties.</summary>
    /// <param name="properties">Simple member-access expressions for columns present in the query result.</param>
    public DuckDBParquetExportOptions<T> PartitionBy(params Expression<Func<T, object?>>[] properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (properties.Length == 0)
        {
            throw new ArgumentException("At least one partition property is required.", nameof(properties));
        }

        foreach (var property in properties)
        {
            _partitionMembers.Add(
                DuckDBTieredStoreExtensions.GetDirectMember(
                    property,
                    nameof(properties),
                    "Partition expressions must be direct property accesses."));
        }

        return this;
    }

    /// <summary>Allows DuckDB to replace partitions already present at the destination.</summary>
    public DuckDBParquetExportOptions<T> OverwriteOrIgnore(bool enable = true)
    {
        OverwriteOrIgnoreEnabled = enable;
        return this;
    }

    /// <summary>Chooses the Parquet compression codec.</summary>
    public DuckDBParquetExportOptions<T> Compression(DuckDBParquetCompression compression)
    {
        CompressionCodec = compression;
        return this;
    }
}

/// <summary>Exports translated EF Core queries through DuckDB's native Parquet writer.</summary>
public static class DuckDBParquetExportExtensions
{
    /// <summary>Exports <paramref name="query" /> to a Parquet file or partitioned directory.</summary>
    public static void ExportToParquet<T>(
        this DatabaseFacade database,
        IQueryable<T> query,
        string path,
        Action<DuckDBParquetExportOptions<T>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var options = CreateOptions(configure);
        using var command = query.CreateDbCommand();
        EnsureSameConnection(database, command.Connection);
        command.CommandText = BuildCopySql(database, command.CommandText, path, options);

        var openedHere = command.Connection!.State != ConnectionState.Open;
        if (openedHere)
        {
            database.OpenConnection();
        }

        try
        {
            command.ExecuteNonQuery();
        }
        finally
        {
            if (openedHere)
            {
                database.CloseConnection();
            }
        }
    }

    /// <summary>Asynchronously exports <paramref name="query" /> to a Parquet file or partitioned directory.</summary>
    public static async Task ExportToParquetAsync<T>(
        this DatabaseFacade database,
        IQueryable<T> query,
        string path,
        Action<DuckDBParquetExportOptions<T>>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var options = CreateOptions(configure);
        await using var command = query.CreateDbCommand();
        EnsureSameConnection(database, command.Connection);
        command.CommandText = BuildCopySql(database, command.CommandText, path, options);

        var openedHere = command.Connection!.State != ConnectionState.Open;
        if (openedHere)
        {
            await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (openedHere)
            {
                await database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    private static DuckDBParquetExportOptions<T> CreateOptions<T>(Action<DuckDBParquetExportOptions<T>>? configure)
    {
        var options = new DuckDBParquetExportOptions<T>();
        configure?.Invoke(options);
        return options;
    }

    private static string BuildCopySql<T>(
        DatabaseFacade database,
        string querySql,
        string path,
        DuckDBParquetExportOptions<T> options)
    {
        var sql = querySql.Trim().TrimEnd(';');
        var clauses = new List<string> { "FORMAT PARQUET" };
        if (options.PartitionMembers.Count > 0)
        {
            var helper = database.GetService<ISqlGenerationHelper>();
            var context = database.GetService<ICurrentDbContext>().Context;
            var columns = options.PartitionMembers.Select(member => ResolveColumnName(context.Model, member));
            clauses.Add($"PARTITION_BY ({string.Join(", ", columns.Select(helper.DelimitIdentifier))})");
        }

        if (options.OverwriteOrIgnoreEnabled)
        {
            clauses.Add("OVERWRITE_OR_IGNORE");
        }

        clauses.Add($"COMPRESSION {CompressionSql(options.CompressionCodec)}");
        return $"COPY ({sql}) TO '{path.Replace("'", "''")}' ({string.Join(", ", clauses)});";
    }

    private static string ResolveColumnName(IModel model, MemberInfo member)
    {
        var entityType = model.FindEntityType(member.DeclaringType!);
        var property = entityType?.FindProperty(member);
        if (entityType?.GetTableName() is { } tableName && property is not null)
        {
            return property.GetColumnName(StoreObjectIdentifier.Table(tableName, entityType.GetSchema())) ?? member.Name;
        }

        return member.Name;
    }

    private static string CompressionSql(DuckDBParquetCompression compression)
        => compression switch
        {
            DuckDBParquetCompression.Snappy => "SNAPPY",
            DuckDBParquetCompression.Zstd => "ZSTD",
            DuckDBParquetCompression.Gzip => "GZIP",
            DuckDBParquetCompression.Uncompressed => "UNCOMPRESSED",
            _ => throw new ArgumentOutOfRangeException(nameof(compression), compression, null)
        };

    private static void EnsureSameConnection(DatabaseFacade database, System.Data.Common.DbConnection? queryConnection)
    {
        if (!ReferenceEquals(database.GetDbConnection(), queryConnection))
        {
            throw new InvalidOperationException(
                "The query and DatabaseFacade must belong to the same DbContext when exporting to Parquet.");
        }
    }
}