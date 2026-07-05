using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System.Globalization;
using System.Text;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in any
///     release.
///     <para>
///         Composes the DuckDB SQL used by the tiered-storage feature (control table, union view, archive
///         <c>COPY</c>, hot-table <c>DELETE</c>, and watermark reads/writes). This type never touches a
///         connection: it only builds SQL strings, which keeps its logic unit-testable in isolation.
///     </para>
/// </summary>
public static class DuckDBTierControl
{
    /// <summary>The name of the provider-managed table that stores each tiered entity's archive watermark.</summary>
    public const string ControlTable = "__duckdb_tier_control";

    private const string TimestampLiteralFormat = "yyyy-MM-dd HH:mm:ss.ffffff";

    /// <summary>
    ///     Aligns an archive cutoff down to the start of its granularity period (month or day). Aligning the
    ///     cutoff guarantees every archived period is written whole in a single offload run, so no Parquet
    ///     partition is ever split across runs.
    /// </summary>
    public static DateTime AlignCutoff(DateTime cutoff, TierGranularity granularity)
        => granularity == TierGranularity.Day
            ? cutoff.Date
            : new DateTime(cutoff.Year, cutoff.Month, 1, 0, 0, 0, cutoff.Kind);

    /// <summary>Builds the <c>CREATE TABLE IF NOT EXISTS</c> statement for the tier control table.</summary>
    public static string ControlTableDdl(ISqlGenerationHelper sql)
        => $"CREATE TABLE IF NOT EXISTS {sql.DelimitIdentifier(ControlTable)} ("
           + "name TEXT PRIMARY KEY, watermark TIMESTAMP, archive_path TEXT, granularity TEXT);";

    /// <summary>Builds the scalar <c>SELECT</c> that reads a tiered entity's current watermark (or <c>NULL</c>).</summary>
    public static string ReadWatermarkSql(ISqlGenerationHelper sql, string controlKey)
        => $"SELECT watermark FROM {sql.DelimitIdentifier(ControlTable)} WHERE name = {Literal(controlKey)};";

    /// <summary>Builds the idempotent <c>INSERT ... ON CONFLICT</c> that records a tiered entity's watermark.</summary>
    public static string UpsertWatermarkSql(
        ISqlGenerationHelper sql,
        string controlKey,
        DateTime watermark,
        string archivePath,
        TierGranularity granularity)
        => $"INSERT INTO {sql.DelimitIdentifier(ControlTable)} (name, watermark, archive_path, granularity) "
           + $"VALUES ({Literal(controlKey)}, {TimestampLiteral(watermark)}, {Literal(NormalizePath(archivePath))}, {Literal(granularity.ToString())}) "
           + "ON CONFLICT (name) DO UPDATE SET watermark = excluded.watermark, "
           + "archive_path = excluded.archive_path, granularity = excluded.granularity;";

    /// <summary>
    ///     Builds the <c>CREATE OR REPLACE VIEW</c> that presents a tiered entity as one logical dataset.
    ///     <para>
    ///         When <paramref name="includeCold" /> is <see langword="false" /> (no archive written yet) the
    ///         view selects only the hot table, which avoids DuckDB's "no files match" error on an empty glob.
    ///         When <see langword="true" />, the hot side is filtered to <c>ts &gt;= watermark</c> and the cold
    ///         side to <c>ts &lt; watermark</c>, so the union never double-counts or drops a row regardless of
    ///         whether the post-archive delete has run.
    ///     </para>
    /// </summary>
    public static string ViewSql(
        ISqlGenerationHelper sql,
        string viewName,
        string hotTable,
        IReadOnlyList<string> columns,
        string timestampColumn,
        string controlKey,
        string archivePath,
        TierGranularity granularity,
        bool includeCold)
    {
        var columnList = string.Join(", ", columns.Select(sql.DelimitIdentifier));
        var builder = new StringBuilder()
            .Append("CREATE OR REPLACE VIEW ").Append(sql.DelimitIdentifier(viewName)).Append(" AS\n");

        if (!includeCold)
        {
            return builder
                .Append("SELECT ").Append(columnList).Append(" FROM ").Append(sql.DelimitIdentifier(hotTable)).Append(';')
                .ToString();
        }

        var watermark = $"(SELECT watermark FROM {sql.DelimitIdentifier(ControlTable)} WHERE name = {Literal(controlKey)})";
        var ts = sql.DelimitIdentifier(timestampColumn);
        // The cold branch selects every archived column except the hive partition columns and lets
        // UNION ALL BY NAME reconcile the two sides. A column added to the entity after old partitions were
        // written is therefore filled with NULL for those rows instead of raising a binder error.
        var excludeList = string.Join(", ", PartitionColumns(granularity).Select(p => sql.DelimitIdentifier(p.Name)));

        return builder
            .Append("SELECT ").Append(columnList).Append(" FROM ").Append(sql.DelimitIdentifier(hotTable))
            .Append("\n  WHERE ").Append(ts).Append(" >= ").Append(watermark)
            .Append("\nUNION ALL BY NAME\n")
            .Append("SELECT * EXCLUDE (").Append(excludeList).Append(')')
            .Append("\n  FROM read_parquet(").Append(Literal(ReadGlob(archivePath)))
            .Append(", hive_partitioning = true, union_by_name = true)")
            .Append("\n  WHERE ").Append(ts).Append(" < ").Append(watermark).Append(';')
            .ToString();
    }

    /// <summary>
    ///     Builds the partitioned <c>COPY ... TO</c> that writes rows in the half-open window
    ///     <c>[from, cutoff)</c> to the cold archive. <c>OVERWRITE_OR_IGNORE</c> replaces only the partition
    ///     directories being written, so incremental runs preserve older months and a crash-retry of the same
    ///     window rewrites its partitions cleanly (no duplicate files).
    /// </summary>
    public static string ArchiveCopySql(
        ISqlGenerationHelper sql,
        string hotTable,
        IReadOnlyList<string> columns,
        string timestampColumn,
        string archivePath,
        TierGranularity granularity,
        DateTime from,
        DateTime cutoff)
    {
        var ts = sql.DelimitIdentifier(timestampColumn);
        var columnList = string.Join(", ", columns.Select(sql.DelimitIdentifier));
        var partitionColumns = PartitionColumns(granularity);
        var partitionSelect = string.Join(
            ", ",
            partitionColumns.Select(p => $"{p.Function}({ts}) AS {sql.DelimitIdentifier(p.Name)}"));
        var partitionBy = string.Join(", ", partitionColumns.Select(p => sql.DelimitIdentifier(p.Name)));

        return $"COPY (SELECT {columnList}, {partitionSelect} FROM {sql.DelimitIdentifier(hotTable)} "
               + $"WHERE {ts} >= {TimestampLiteral(from)} AND {ts} < {TimestampLiteral(cutoff)}) "
               + $"TO {Literal(NormalizePath(archivePath))} "
               + $"(FORMAT PARQUET, PARTITION_BY ({partitionBy}), OVERWRITE_OR_IGNORE);";
    }

    /// <summary>Builds the hot-table <c>DELETE</c> that reclaims space for rows now safely in the archive.</summary>
    public static string DeleteHotSql(ISqlGenerationHelper sql, string hotTable, string timestampColumn, DateTime cutoff)
        => $"DELETE FROM {sql.DelimitIdentifier(hotTable)} WHERE {sql.DelimitIdentifier(timestampColumn)} < {TimestampLiteral(cutoff)};";

    /// <summary>
    ///     One hop of a child→…→root foreign-key chain: the dependent's foreign-key column, the principal
    ///     table joined to, and the principal's key column.
    /// </summary>
    public readonly record struct TierJoinHop(string ForeignKeyColumn, string PrincipalTable, string PrincipalKeyColumn);

    /// <summary>
    ///     Builds the partitioned <c>COPY ... TO</c> for an aggregate <em>child</em>: its rows are joined up the
    ///     FK <paramref name="chain" /> to the root so the boundary and hive partitioning use the root's date.
    /// </summary>
    public static string ArchiveChildCopySql(
        ISqlGenerationHelper sql,
        string childTable,
        IReadOnlyList<string> childColumns,
        IReadOnlyList<TierJoinHop> chain,
        string rootTimestampColumn,
        string archivePath,
        TierGranularity granularity,
        DateTime from,
        DateTime cutoff)
    {
        var columnList = string.Join(", ", childColumns.Select(c => "t0." + sql.DelimitIdentifier(c)));
        var rootAlias = "t" + chain.Count;
        var rootTs = rootAlias + "." + sql.DelimitIdentifier(rootTimestampColumn);
        var partitionColumns = PartitionColumns(granularity);
        var partitionSelect = string.Join(", ", partitionColumns.Select(p => $"{p.Function}({rootTs}) AS {sql.DelimitIdentifier(p.Name)}"));
        var partitionBy = string.Join(", ", partitionColumns.Select(p => sql.DelimitIdentifier(p.Name)));

        var joins = new StringBuilder("FROM ").Append(sql.DelimitIdentifier(childTable)).Append(" AS t0");
        for (var i = 0; i < chain.Count; i++)
        {
            joins.Append(" JOIN ").Append(sql.DelimitIdentifier(chain[i].PrincipalTable)).Append(" AS t").Append(i + 1)
                .Append(" ON t").Append(i).Append('.').Append(sql.DelimitIdentifier(chain[i].ForeignKeyColumn))
                .Append(" = t").Append(i + 1).Append('.').Append(sql.DelimitIdentifier(chain[i].PrincipalKeyColumn));
        }

        return $"COPY (SELECT {columnList}, {partitionSelect} {joins} "
               + $"WHERE {rootTs} >= {TimestampLiteral(from)} AND {rootTs} < {TimestampLiteral(cutoff)}) "
               + $"TO {Literal(NormalizePath(archivePath))} "
               + $"(FORMAT PARQUET, PARTITION_BY ({partitionBy}), OVERWRITE_OR_IGNORE);";
    }

    /// <summary>
    ///     Builds the union view for an aggregate <em>child</em>. A child row is hot iff its root is on the hot
    ///     side of the watermark, expressed as a semijoin that chains up the FK <paramref name="chain" />; the
    ///     cold branch reads every archived child (all are below the watermark by construction).
    /// </summary>
    public static string ChildViewSql(
        ISqlGenerationHelper sql,
        string viewName,
        string childTable,
        IReadOnlyList<string> childColumns,
        IReadOnlyList<TierJoinHop> chain,
        string rootTimestampColumn,
        string controlKey,
        string archivePath,
        TierGranularity granularity,
        bool includeCold,
        bool includeHotChildFilter)
    {
        var columnList = string.Join(", ", childColumns.Select(sql.DelimitIdentifier));
        var builder = new StringBuilder()
            .Append("CREATE OR REPLACE VIEW ").Append(sql.DelimitIdentifier(viewName)).Append(" AS\n")
            .Append("SELECT ").Append(columnList).Append(" FROM ").Append(sql.DelimitIdentifier(childTable));

        if (includeHotChildFilter && includeCold)
        {
            var watermark = $">= (SELECT watermark FROM {sql.DelimitIdentifier(ControlTable)} WHERE name = {Literal(controlKey)})";
            builder.Append("\n  WHERE ").Append(sql.DelimitIdentifier(chain[0].ForeignKeyColumn))
                .Append(" IN (").Append(RootSemijoin(sql, chain, rootTimestampColumn, 0, watermark)).Append(')');
        }

        if (!includeCold)
        {
            return builder.Append(';').ToString();
        }

        var excludeList = string.Join(", ", PartitionColumns(granularity).Select(p => sql.DelimitIdentifier(p.Name)));
        return builder
            .Append("\nUNION ALL BY NAME\n")
            .Append("SELECT * EXCLUDE (").Append(excludeList).Append(')')
            .Append("\n  FROM read_parquet(").Append(Literal(ReadGlob(archivePath)))
            .Append(", hive_partitioning = true, union_by_name = true);")
            .ToString();
    }

    /// <summary>
    ///     Builds the child <c>DELETE</c> for rows whose root has aged past the cutoff, matched by chaining the
    ///     FK <paramref name="chain" /> up to the root. Run leaf→root so foreign keys stay satisfied.
    /// </summary>
    public static string DeleteChildSql(
        ISqlGenerationHelper sql,
        string childTable,
        IReadOnlyList<TierJoinHop> chain,
        string rootTimestampColumn,
        DateTime cutoff)
        => $"DELETE FROM {sql.DelimitIdentifier(childTable)} WHERE {sql.DelimitIdentifier(chain[0].ForeignKeyColumn)} "
           + $"IN ({RootSemijoin(sql, chain, rootTimestampColumn, 0, "< " + TimestampLiteral(cutoff))});";

    // Nested semijoin from the child's foreign key up the chain to the root, ending in "<rootTs> <rootCondition>".
    private static string RootSemijoin(
        ISqlGenerationHelper sql,
        IReadOnlyList<TierJoinHop> chain,
        string rootTimestampColumn,
        int level,
        string rootCondition)
    {
        var hop = chain[level];
        var select = $"SELECT {sql.DelimitIdentifier(hop.PrincipalKeyColumn)} FROM {sql.DelimitIdentifier(hop.PrincipalTable)} WHERE ";
        return level == chain.Count - 1
            ? select + $"{sql.DelimitIdentifier(rootTimestampColumn)} {rootCondition}"
            : select + $"{sql.DelimitIdentifier(chain[level + 1].ForeignKeyColumn)} IN ({RootSemijoin(sql, chain, rootTimestampColumn, level + 1, rootCondition)})";
    }

    /// <summary>The recursive Parquet glob that matches every archived partition file under the archive root.</summary>
    public static string ReadGlob(string archivePath) => NormalizePath(archivePath) + "/**/*.parquet";

    private static (string Function, string Name)[] PartitionColumns(TierGranularity granularity)
        => granularity == TierGranularity.Day
            ? [("year", "year"), ("month", "month"), ("day", "day")]
            : [("year", "year"), ("month", "month")];

    private static string NormalizePath(string path) => path.TrimEnd('/', '\\');

    private static string TimestampLiteral(DateTime value)
        => $"TIMESTAMP {Literal(value.ToString(TimestampLiteralFormat, CultureInfo.InvariantCulture))}";

    private static string Literal(string value) => $"'{value.Replace("'", "''")}'";
}
