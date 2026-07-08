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
    ///         When <see langword="true" />, the cold side is filtered to <c>ts &lt; watermark</c> and the hot
    ///         side anti-joins against cold rows by primary key, so crash-retry duplicates are hidden while
    ///         genuinely late/backdated hot rows remain visible until they are archived.
    ///     </para>
    /// </summary>
    public static string ViewSql(
        ISqlGenerationHelper sql,
        string viewName,
        string hotTable,
        string? hotSchema,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> keyColumns,
        string timestampColumn,
        string controlKey,
        string archivePath,
        TierGranularity granularity,
        bool includeCold)
    {
        const string hotAlias = "h";
        var columnList = ColumnList(sql, columns, hotAlias);
        var builder = new StringBuilder()
            .Append("CREATE OR REPLACE VIEW ").Append(sql.DelimitIdentifier(viewName)).Append(" AS\n");

        if (!includeCold)
        {
            return builder
                .Append("SELECT ").Append(columnList).Append(" FROM ").Append(Table(sql, hotTable, hotSchema)).Append(" AS ").Append(hotAlias).Append(';')
                .ToString();
        }

        EnsureKeyColumns(keyColumns, hotTable);
        var watermark = $"(SELECT watermark FROM {sql.DelimitIdentifier(ControlTable)} WHERE name = {Literal(controlKey)})";
        var ts = sql.DelimitIdentifier(timestampColumn);
        // The cold branch selects every archived column except the hive partition columns and lets
        // UNION ALL BY NAME reconcile the two sides. A column added to the entity after old partitions were
        // written is therefore filled with NULL for those rows instead of raising a binder error.
        var excludeList = string.Join(", ", PartitionColumns(granularity).Select(p => sql.DelimitIdentifier(p.Name)));
        var hotKeyMatch = KeyMatchPredicate(sql, keyColumns, "c", hotAlias);

        return builder
            .Append("WITH cold AS (\n")
            .Append("SELECT * EXCLUDE (").Append(excludeList).Append(')')
            .Append("\n  FROM read_parquet(").Append(Literal(ReadGlob(archivePath)))
            .Append(", hive_partitioning = true, union_by_name = true)")
            .Append("\n),\nvisible_cold AS (\n")
            .Append("SELECT * FROM cold WHERE ").Append(ts).Append(" < ").Append(watermark)
            .Append("\n)\n")
            .Append("SELECT ").Append(columnList).Append(" FROM ").Append(Table(sql, hotTable, hotSchema)).Append(" AS ").Append(hotAlias)
            .Append("\n  WHERE NOT EXISTS (SELECT 1 FROM visible_cold AS c WHERE ").Append(hotKeyMatch).Append(')')
            .Append("\nUNION ALL BY NAME\n")
            .Append("SELECT * FROM visible_cold;")
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
        string? hotSchema,
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

        return $"COPY (SELECT {columnList}, {partitionSelect} FROM {Table(sql, hotTable, hotSchema)} "
               + $"WHERE {ts} >= {TimestampLiteral(from)} AND {ts} < {TimestampLiteral(cutoff)}) "
               + $"TO {Literal(NormalizePath(archivePath))} "
               + $"(FORMAT PARQUET, PARTITION_BY ({partitionBy}), OVERWRITE_OR_IGNORE);";
    }

    /// <summary>
    ///     Builds the hot-table <c>DELETE</c> that reclaims space only for rows already present in the archive.
    /// </summary>
    public static string DeleteHotSql(
        ISqlGenerationHelper sql,
        string hotTable,
        string? hotSchema,
        IReadOnlyList<string> keyColumns,
        string timestampColumn,
        string archivePath,
        DateTime cutoff)
    {
        const string hotAlias = "h";
        EnsureKeyColumns(keyColumns, hotTable);
        var ts = sql.DelimitIdentifier(timestampColumn);
        return $"DELETE FROM {Table(sql, hotTable, hotSchema)} AS {hotAlias} WHERE {hotAlias}.{ts} < {TimestampLiteral(cutoff)} "
               + $"AND EXISTS (SELECT 1 FROM read_parquet({Literal(ReadGlob(archivePath))}, hive_partitioning = true, union_by_name = true) AS c "
               + $"WHERE {KeyMatchPredicate(sql, keyColumns, "c", hotAlias)} AND c.{ts} < {TimestampLiteral(cutoff)});";
    }

    /// <summary>
    ///     One hop of a child→…→root foreign-key chain: the dependent's foreign-key column, the principal
    ///     table joined to, and the principal's key column.
    /// </summary>
    public readonly record struct TierJoinHop(string ForeignKeyColumn, string PrincipalTable, string? PrincipalSchema, string PrincipalKeyColumn);

    /// <summary>
    ///     Builds the partitioned <c>COPY ... TO</c> for an aggregate <em>child</em>: its rows are joined up the
    ///     FK <paramref name="chain" /> to the root so the boundary and hive partitioning use the root's date.
    /// </summary>
    public static string ArchiveChildCopySql(
        ISqlGenerationHelper sql,
        string childTable,
        string? childSchema,
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

        var joins = new StringBuilder("FROM ").Append(Table(sql, childTable, childSchema)).Append(" AS t0");
        for (var i = 0; i < chain.Count; i++)
        {
            joins.Append(" JOIN ").Append(Table(sql, chain[i].PrincipalTable, chain[i].PrincipalSchema)).Append(" AS t").Append(i + 1)
                .Append(" ON t").Append(i).Append('.').Append(sql.DelimitIdentifier(chain[i].ForeignKeyColumn))
                .Append(" = t").Append(i + 1).Append('.').Append(sql.DelimitIdentifier(chain[i].PrincipalKeyColumn));
        }

        return $"COPY (SELECT {columnList}, {partitionSelect} {joins} "
               + $"WHERE {rootTs} >= {TimestampLiteral(from)} AND {rootTs} < {TimestampLiteral(cutoff)}) "
               + $"TO {Literal(NormalizePath(archivePath))} "
               + $"(FORMAT PARQUET, PARTITION_BY ({partitionBy}), OVERWRITE_OR_IGNORE);";
    }

    /// <summary>
    ///     Builds the union view for an aggregate <em>child</em>. Hot rows anti-join against cold rows by
    ///     primary key, with the existing root-hot semijoin preserved as a fast positive path when enabled.
    ///     The cold branch reads every archived child because child rows are below the watermark by construction.
    /// </summary>
    public static string ChildViewSql(
        ISqlGenerationHelper sql,
        string viewName,
        string childTable,
        string? childSchema,
        IReadOnlyList<string> childColumns,
        IReadOnlyList<string> childKeyColumns,
        IReadOnlyList<TierJoinHop> chain,
        string rootTimestampColumn,
        string controlKey,
        string archivePath,
        TierGranularity granularity,
        bool includeCold,
        bool includeHotChildFilter)
    {
        const string hotAlias = "h";
        var columnList = ColumnList(sql, childColumns, hotAlias);
        var viewHeader = new StringBuilder()
            .Append("CREATE OR REPLACE VIEW ").Append(sql.DelimitIdentifier(viewName)).Append(" AS\n");

        if (!includeCold)
        {
            return viewHeader
                .Append("SELECT ").Append(columnList).Append(" FROM ").Append(Table(sql, childTable, childSchema)).Append(" AS ").Append(hotAlias).Append(';')
                .ToString();
        }

        EnsureKeyColumns(childKeyColumns, childTable);
        var excludeList = string.Join(", ", PartitionColumns(granularity).Select(p => sql.DelimitIdentifier(p.Name)));
        var hotKeyMatch = KeyMatchPredicate(sql, childKeyColumns, "c", hotAlias);
        var hotPredicate = new StringBuilder()
            .Append("NOT EXISTS (SELECT 1 FROM cold AS c WHERE ").Append(hotKeyMatch).Append(')');

        if (includeHotChildFilter)
        {
            var watermark = $">= (SELECT watermark FROM {sql.DelimitIdentifier(ControlTable)} WHERE name = {Literal(controlKey)})";
            hotPredicate.Insert(
                0,
                hotAlias + "." + sql.DelimitIdentifier(chain[0].ForeignKeyColumn)
                + " IN (" + RootSemijoin(sql, chain, rootTimestampColumn, 0, watermark) + ") OR ");
        }

        return viewHeader
            .Append("WITH cold AS (\n")
            .Append("SELECT * EXCLUDE (").Append(excludeList).Append(')')
            .Append("\n  FROM read_parquet(").Append(Literal(ReadGlob(archivePath)))
            .Append(", hive_partitioning = true, union_by_name = true)")
            .Append("\n)\n")
            .Append("SELECT ").Append(columnList).Append(" FROM ").Append(Table(sql, childTable, childSchema)).Append(" AS ").Append(hotAlias)
            .Append("\n  WHERE ").Append(hotPredicate)
            .Append("\nUNION ALL BY NAME\n")
            .Append("SELECT * FROM cold;")
            .ToString();
    }

    /// <summary>
    ///     Builds the child <c>DELETE</c> for rows whose root has aged past the cutoff, matched by chaining the
    ///     FK <paramref name="chain" /> up to the root. Run leaf→root so foreign keys stay satisfied.
    /// </summary>
    public static string DeleteChildSql(
        ISqlGenerationHelper sql,
        string childTable,
        string? childSchema,
        IReadOnlyList<string> childKeyColumns,
        IReadOnlyList<TierJoinHop> chain,
        string rootTimestampColumn,
        string archivePath,
        DateTime cutoff)
    {
        const string childAlias = "h";
        EnsureKeyColumns(childKeyColumns, childTable);
        return $"DELETE FROM {Table(sql, childTable, childSchema)} AS {childAlias} WHERE {childAlias}.{sql.DelimitIdentifier(chain[0].ForeignKeyColumn)} "
               + $"IN ({RootSemijoin(sql, chain, rootTimestampColumn, 0, "< " + TimestampLiteral(cutoff))}) "
               + $"AND EXISTS (SELECT 1 FROM read_parquet({Literal(ReadGlob(archivePath))}, hive_partitioning = true, union_by_name = true) AS c "
               + $"WHERE {KeyMatchPredicate(sql, childKeyColumns, "c", childAlias)});";
    }

    // Nested semijoin from the child's foreign key up the chain to the root, ending in "<rootTs> <rootCondition>".
    private static string RootSemijoin(
        ISqlGenerationHelper sql,
        IReadOnlyList<TierJoinHop> chain,
        string rootTimestampColumn,
        int level,
        string rootCondition)
    {
        var hop = chain[level];
        var alias = "r" + level.ToString(CultureInfo.InvariantCulture);
        var select = $"SELECT {alias}.{sql.DelimitIdentifier(hop.PrincipalKeyColumn)} FROM {Table(sql, hop.PrincipalTable, hop.PrincipalSchema)} AS {alias} WHERE ";
        return level == chain.Count - 1
            ? select + $"{alias}.{sql.DelimitIdentifier(rootTimestampColumn)} {rootCondition}"
            : select + $"{alias}.{sql.DelimitIdentifier(chain[level + 1].ForeignKeyColumn)} IN ({RootSemijoin(sql, chain, rootTimestampColumn, level + 1, rootCondition)})";
    }

    /// <summary>The recursive Parquet glob that matches every archived partition file under the archive root.</summary>
    public static string ReadGlob(string archivePath) => NormalizePath(archivePath) + "/**/*.parquet";

    private static void EnsureKeyColumns(IReadOnlyList<string> keyColumns, string table)
    {
        if (keyColumns.Count == 0)
        {
            throw new InvalidOperationException($"Tiered-storage table '{table}' must have a primary key.");
        }
    }

    private static string KeyMatchPredicate(
        ISqlGenerationHelper sql,
        IReadOnlyList<string> keyColumns,
        string leftAlias,
        string rightAlias)
        => string.Join(
            " AND ",
            keyColumns.Select(column => $"{leftAlias}.{sql.DelimitIdentifier(column)} = {rightAlias}.{sql.DelimitIdentifier(column)}"));

    private static string ColumnList(ISqlGenerationHelper sql, IReadOnlyList<string> columns, string alias)
        => string.Join(", ", columns.Select(column => $"{alias}.{sql.DelimitIdentifier(column)}"));

    private static string Table(ISqlGenerationHelper sql, string table, string? schema)
        => sql.DelimitIdentifier(table, schema);

    private static (string Function, string Name)[] PartitionColumns(TierGranularity granularity)
        => granularity == TierGranularity.Day
            ? [("year", "year"), ("month", "month"), ("day", "day")]
            : [("year", "year"), ("month", "month")];

    private static string NormalizePath(string path) => path.TrimEnd('/', '\\');

    private static string TimestampLiteral(DateTime value)
        => $"TIMESTAMP {Literal(value.ToString(TimestampLiteralFormat, CultureInfo.InvariantCulture))}";

    private static string Literal(string value) => $"'{value.Replace("'", "''")}'";
}
