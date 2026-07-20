using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>DuckLake-specific query roots for table-scoped historical reads.</summary>
public static class DuckLakeQueryableExtensions
{
    /// <summary>Queries one mapped DuckLake table at a committed snapshot.</summary>
    /// <remarks>
    ///     This pins only this query root. Other roots in a composed query remain current unless pinned separately.
    ///     Use a catalog-wide <see cref="Infrastructure.DuckLakeDbContextOptionsBuilder.AsOfSnapshot" /> profile
    ///     when every table in a query must use the same snapshot.
    /// </remarks>
    /// <typeparam name="TEntity">The mapped entity type.</typeparam>
    /// <param name="source">The uncomposed entity query root.</param>
    /// <param name="snapshotId">The non-negative DuckLake snapshot identifier.</param>
    /// <returns>A composable query rooted at the selected table snapshot.</returns>
    public static IQueryable<TEntity> AsOfSnapshot<TEntity>(
        this DbSet<TEntity> source,
        long snapshotId)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(snapshotId);
        return CreateHistoricalQuery(source, "VERSION", snapshotId);
    }

    /// <summary>Queries one mapped DuckLake table at the latest snapshot at or before a timestamp.</summary>
    /// <remarks>
    ///     This pins only this query root. Other roots in a composed query remain current unless pinned separately.
    ///     Use a catalog-wide <see cref="Infrastructure.DuckLakeDbContextOptionsBuilder.AsOfTimestamp" /> profile
    ///     when every table in a query must use the same timestamp.
    /// </remarks>
    /// <typeparam name="TEntity">The mapped entity type.</typeparam>
    /// <param name="source">The uncomposed entity query root.</param>
    /// <param name="timestamp">The point in time used by DuckLake to select a snapshot.</param>
    /// <returns>A composable query rooted at the selected table snapshot.</returns>
    public static IQueryable<TEntity> AsOfTimestamp<TEntity>(
        this DbSet<TEntity> source,
        DateTimeOffset timestamp)
        where TEntity : class
        => CreateHistoricalQuery(source, "TIMESTAMP", timestamp);

    private static IQueryable<TEntity> CreateHistoricalQuery<TEntity>(
        DbSet<TEntity> source,
        string selector,
        object value)
        where TEntity : class
    {
        var context = source.GetService<ICurrentDbContext>().Context;
        var profile = context.GetService<IDbContextOptions>()
            .FindExtension<DuckDBOptionsExtension>()?
            .DuckLakeOptions
            ?? throw new InvalidOperationException(
                "DuckLake time-travel query roots require a context configured with UseDuckLake(...).");

        if (profile.SnapshotVersion is not null || profile.SnapshotTime is not null)
        {
            throw new InvalidOperationException(
                "Table-scoped time travel cannot be combined with a catalog-wide historical DuckLake profile.");
        }

        var entityType = context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"Entity type '{typeof(TEntity).Name}' is not part of the current model.");
        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException(
                $"Entity type '{entityType.DisplayName()}' is not mapped to a table and cannot use DuckLake time travel.");
        var schemaName = entityType.GetSchema() ?? "main";
        var qualifiedTable = string.Join(
            '.',
            DelimitIdentifier(profile.CatalogName),
            DelimitIdentifier(schemaName),
            DelimitIdentifier(tableName));

        // The only interpolated SQL fragments are provider/model identifiers delimited above and the fixed selector.
        // The caller-supplied snapshot value remains an EF parameter through the {0} placeholder.
#pragma warning disable EF1002
        return source.FromSqlRaw($"SELECT * FROM {qualifiedTable} AT ({selector} => {{0}})", value);
#pragma warning restore EF1002
    }

    private static string DelimitIdentifier(string identifier)
        => '"' + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
}