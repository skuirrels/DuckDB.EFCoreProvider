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
           + "name TEXT PRIMARY KEY, watermark TIMESTAMP, archive_path TEXT, granularity TEXT, partition_spec TEXT); "
           + $"ALTER TABLE {sql.DelimitIdentifier(ControlTable)} ADD COLUMN IF NOT EXISTS partition_spec TEXT;";

    /// <summary>Builds the scalar <c>SELECT</c> that reads a tiered entity's current watermark (or <c>NULL</c>).</summary>
    public static string ReadWatermarkSql(ISqlGenerationHelper sql, string controlKey)
        => $"SELECT watermark FROM {sql.DelimitIdentifier(ControlTable)} WHERE name = {Literal(controlKey)};";

    /// <summary>Builds the scalar <c>SELECT</c> that reads the persisted additional-partition layout signature.</summary>
    public static string ReadPartitionSpecSql(ISqlGenerationHelper sql, string controlKey)
        => $"SELECT partition_spec FROM {sql.DelimitIdentifier(ControlTable)} WHERE name = {Literal(controlKey)};";

    /// <summary>Builds the scalar <c>SELECT</c> that reads the persisted archive granularity.</summary>
    public static string ReadGranularitySql(ISqlGenerationHelper sql, string controlKey)
        => $"SELECT granularity FROM {sql.DelimitIdentifier(ControlTable)} WHERE name = {Literal(controlKey)};";

    /// <summary>
    ///     Persists the configured physical partition layout without advancing the archive watermark. Recording
    ///     this before <c>COPY</c> makes the layout recoverable if the process stops before the final watermark upsert.
    /// </summary>
    public static string UpsertPartitionLayoutSql(
        ISqlGenerationHelper sql,
        string controlKey,
        string archivePath,
        TierGranularity granularity,
        string partitionSpec)
        => $"INSERT INTO {sql.DelimitIdentifier(ControlTable)} (name, archive_path, granularity, partition_spec) "
           + $"VALUES ({Literal(controlKey)}, {Literal(NormalizePath(archivePath))}, {Literal(granularity.ToString())}, {Literal(partitionSpec)}) "
           + "ON CONFLICT (name) DO UPDATE SET archive_path = excluded.archive_path, "
           + "granularity = excluded.granularity, partition_spec = excluded.partition_spec;";

    /// <summary>Builds the idempotent <c>INSERT ... ON CONFLICT</c> that records a tiered entity's watermark.</summary>
    public static string UpsertWatermarkSql(
        ISqlGenerationHelper sql,
        string controlKey,
        DateTime watermark,
        string archivePath,
        TierGranularity granularity)
        => UpsertWatermarkSql(sql, controlKey, watermark, archivePath, granularity, "[]");

    /// <summary>Builds the idempotent <c>INSERT ... ON CONFLICT</c> that records a tiered entity's watermark and partition layout.</summary>
    public static string UpsertWatermarkSql(
        ISqlGenerationHelper sql,
        string controlKey,
        DateTime watermark,
        string archivePath,
        TierGranularity granularity,
        string partitionSpec)
        => $"INSERT INTO {sql.DelimitIdentifier(ControlTable)} (name, watermark, archive_path, granularity, partition_spec) "
           + $"VALUES ({Literal(controlKey)}, {TimestampLiteral(watermark)}, {Literal(NormalizePath(archivePath))}, {Literal(granularity.ToString())}, {Literal(partitionSpec)}) "
           + "ON CONFLICT (name) DO UPDATE SET watermark = excluded.watermark, "
           + "archive_path = excluded.archive_path, granularity = excluded.granularity, partition_spec = excluded.partition_spec;";

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
        => ViewSql(
            sql,
            viewName,
            hotTable,
            hotSchema,
            columns,
            keyColumns,
            timestampColumn,
            controlKey,
            archivePath,
            granularity,
            includeCold,
            null);

    /// <summary>
    ///     Builds the <c>CREATE OR REPLACE VIEW</c> for a root whose hive partition columns need their
    ///     configured relational types restored.
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
        bool includeCold,
        IReadOnlyList<DuckDBTierPartitionColumn>? rootPartitions)
    {
        const string hotAlias = "h";
        var hotProjection = HotProjection(sql, columns, hotAlias, rootPartitions);
        var builder = new StringBuilder()
            .Append("CREATE OR REPLACE VIEW ").Append(sql.DelimitIdentifier(viewName)).Append(" AS\n");

        if (!includeCold)
        {
            return builder
                .Append("SELECT ").Append(hotProjection).Append(" FROM ").Append(Table(sql, hotTable, hotSchema)).Append(" AS ").Append(hotAlias).Append(';')
                .ToString();
        }

        EnsureKeyColumns(keyColumns, hotTable);
        var watermark = $"(SELECT watermark FROM {sql.DelimitIdentifier(ControlTable)} WHERE name = {Literal(controlKey)})";
        var ts = sql.DelimitIdentifier(timestampColumn);
        // The cold branch selects every archived column except the hive partition columns and lets
        // UNION ALL BY NAME reconcile the two sides. A column added to the entity after old partitions were
        // written is therefore filled with NULL for those rows instead of raising a binder error.
        var coldProjection = StarProjection(
            sql,
            rootPartitions is { Count: > 0 }
                ? []
                : TemporalPartitionColumns(granularity).Select(partition => partition.Name),
            rootPartitions);
        var hotKeyMatch = KeyMatchPredicate(sql, keyColumns, "c", hotAlias);
        var coldSource = new StringBuilder("(SELECT ")
            .Append(coldProjection)
            .Append(" FROM read_parquet(").Append(Literal(ReadGlob(archivePath)))
            .Append(", hive_partitioning = true, union_by_name = true))")
            .ToString();

        return builder
            .Append("SELECT ").Append(hotProjection).Append(" FROM ").Append(Table(sql, hotTable, hotSchema)).Append(" AS ").Append(hotAlias)
            .Append("\n  WHERE ").Append(hotAlias).Append('.').Append(ts).Append(" >= ").Append(watermark)
            .Append("\nUNION ALL BY NAME\n")
            .Append("SELECT ").Append(hotProjection).Append(" FROM ").Append(Table(sql, hotTable, hotSchema)).Append(" AS ").Append(hotAlias)
            .Append("\n  WHERE ").Append(hotAlias).Append('.').Append(ts).Append(" < ").Append(watermark)
            .Append(" AND NOT EXISTS (SELECT 1 FROM ").Append(coldSource).Append(" AS c WHERE c.").Append(ts)
            .Append(" < ").Append(watermark).Append(" AND ").Append(hotKeyMatch).Append(')')
            .Append("\nUNION ALL BY NAME\n")
            .Append("SELECT * FROM ").Append(coldSource).Append(" AS c WHERE c.").Append(ts).Append(" < ").Append(watermark).Append(';')
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
        => ArchiveCopySql(
            sql,
            hotTable,
            hotSchema,
            columns,
            timestampColumn,
            archivePath,
            granularity,
            from,
            cutoff,
            null);

    /// <summary>
    ///     Builds the partitioned <c>COPY ... TO</c> with additional aggregate-root partition columns.
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
        DateTime cutoff,
        IReadOnlyList<DuckDBTierPartitionColumn>? rootPartitions)
    {
        var ts = sql.DelimitIdentifier(timestampColumn);
        var columnList = string.Join(", ", columns.Select(sql.DelimitIdentifier));
        var partitionSelect = rootPartitions is { Count: > 0 }
            ? rootPartitions.Where(partition => partition.Transform != TierPartitionTransform.Value)
                .Select(partition => PartitionSelect(sql, partition, sql.DelimitIdentifier(partition.SourceColumn)))
            : TemporalPartitionColumns(granularity)
                .Select(partition => $"{partition.Function}({ts}) AS {sql.DelimitIdentifier(partition.Name)}");
        var partitionBy = string.Join(", ", PartitionColumns(granularity, rootPartitions).Select(sql.DelimitIdentifier));
        var copyProjection = AppendColumns(columnList, partitionSelect);

        return $"COPY (SELECT {copyProjection} FROM {Table(sql, hotTable, hotSchema)} "
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
        => DeleteHotSql(sql, hotTable, hotSchema, keyColumns, timestampColumn, archivePath, cutoff, null);

    /// <summary>
    ///     Builds the hot-table <c>DELETE</c> and restores aggregate-root hive keys to their relational types.
    /// </summary>
    public static string DeleteHotSql(
        ISqlGenerationHelper sql,
        string hotTable,
        string? hotSchema,
        IReadOnlyList<string> keyColumns,
        string timestampColumn,
        string archivePath,
        DateTime cutoff,
        IReadOnlyList<DuckDBTierPartitionColumn>? rootPartitions)
    {
        const string hotAlias = "h";
        EnsureKeyColumns(keyColumns, hotTable);
        var ts = sql.DelimitIdentifier(timestampColumn);
        return $"DELETE FROM {Table(sql, hotTable, hotSchema)} AS {hotAlias} WHERE {hotAlias}.{ts} < {TimestampLiteral(cutoff)} "
               + $"AND EXISTS (SELECT 1 FROM {TypedParquetRead(sql, archivePath, rootPartitions)} AS c "
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
        => ArchiveChildCopySql(
            sql,
            childTable,
            childSchema,
            childColumns,
            chain,
            rootTimestampColumn,
            archivePath,
            granularity,
            from,
            cutoff,
            null);

    /// <summary>
    ///     Builds the partitioned <c>COPY ... TO</c> for an aggregate child using additional root partitions.
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
        DateTime cutoff,
        IReadOnlyList<DuckDBTierPartitionColumn>? rootPartitions)
    {
        var columnList = string.Join(", ", childColumns.Select(c => "t0." + sql.DelimitIdentifier(c)));
        var rootAlias = "t" + chain.Count;
        var rootTs = rootAlias + "." + sql.DelimitIdentifier(rootTimestampColumn);
        var partitionSelect = rootPartitions is { Count: > 0 }
            ? rootPartitions.Select(partition => PartitionSelect(
                sql,
                partition,
                $"{rootAlias}.{sql.DelimitIdentifier(partition.SourceColumn)}"))
            : TemporalPartitionColumns(granularity)
                .Select(p => $"{p.Function}({rootTs}) AS {sql.DelimitIdentifier(p.Name)}");
        var partitionBy = string.Join(", ",
            PartitionColumns(granularity, rootPartitions).Select(sql.DelimitIdentifier));

        var joins = new StringBuilder("FROM ").Append(Table(sql, childTable, childSchema)).Append(" AS t0");
        for (var i = 0; i < chain.Count; i++)
        {
            joins.Append(" JOIN ").Append(Table(sql, chain[i].PrincipalTable, chain[i].PrincipalSchema)).Append(" AS t").Append(i + 1)
                .Append(" ON t").Append(i).Append('.').Append(sql.DelimitIdentifier(chain[i].ForeignKeyColumn))
                .Append(" = t").Append(i + 1).Append('.').Append(sql.DelimitIdentifier(chain[i].PrincipalKeyColumn));
        }

        return $"COPY (SELECT {columnList}, {string.Join(", ", partitionSelect)} {joins} "
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
        => ChildViewSql(
            sql,
            viewName,
            childTable,
            childSchema,
            childColumns,
            childKeyColumns,
            chain,
            rootTimestampColumn,
            controlKey,
            archivePath,
            granularity,
            includeCold,
            includeHotChildFilter,
            null);

    /// <summary>
    ///     Builds the union view for an aggregate child archived beneath additional root partitions.
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
        bool includeHotChildFilter,
        IReadOnlyList<DuckDBTierPartitionColumn>? rootPartitions)
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
        var excludeList = string.Join(", ",
            PartitionColumns(granularity, rootPartitions).Select(sql.DelimitIdentifier));
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

    private static string HotProjection(
        ISqlGenerationHelper sql,
        IReadOnlyList<string> columns,
        string alias,
        IReadOnlyList<DuckDBTierPartitionColumn>? partitions)
        => AppendColumns(
            ColumnList(sql, columns, alias),
            (partitions ?? []).Where(partition => partition.Transform != TierPartitionTransform.Value)
                .Select(partition => PartitionSelect(
                    sql,
                    partition,
                    $"{alias}.{sql.DelimitIdentifier(partition.SourceColumn)}")));

    private static string Table(ISqlGenerationHelper sql, string table, string? schema)
        => sql.DelimitIdentifier(table, schema);

    private static string StarProjection(
        ISqlGenerationHelper sql,
        IEnumerable<string> excludedColumns,
        IReadOnlyList<DuckDBTierPartitionColumn>? typedPartitions)
    {
        var excluded = excludedColumns.ToArray();
        var builder = new StringBuilder("*");
        if (excluded.Length > 0)
        {
            builder.Append(" EXCLUDE (")
                .Append(string.Join(", ", excluded.Select(sql.DelimitIdentifier)))
                .Append(')');
        }

        if (typedPartitions is { Count: > 0 })
        {
            builder.Append(" REPLACE (")
                .Append(string.Join(", ", typedPartitions.Select(partition =>
                {
                    var column = sql.DelimitIdentifier(partition.Name);
                    return $"CAST({column} AS {partition.StoreType}) AS {column}";
                })))
                .Append(')');
        }

        return builder.ToString();
    }

    private static string TypedParquetRead(
        ISqlGenerationHelper sql,
        string archivePath,
        IReadOnlyList<DuckDBTierPartitionColumn>? typedPartitions)
    {
        var read = $"read_parquet({Literal(ReadGlob(archivePath))}, hive_partitioning = true, union_by_name = true)";
        return typedPartitions is { Count: > 0 }
            ? $"(SELECT {StarProjection(sql, [], typedPartitions)} FROM {read})"
            : read;
    }

    private static (string Function, string Name)[] TemporalPartitionColumns(TierGranularity granularity)
        => granularity == TierGranularity.Day
            ? [("year", "year"), ("month", "month"), ("day", "day")]
            : [("year", "year"), ("month", "month")];

    private static IEnumerable<string> PartitionColumns(
        TierGranularity granularity,
        IReadOnlyList<DuckDBTierPartitionColumn>? partitions)
        => partitions is { Count: > 0 }
            ? partitions.Select(partition => partition.Name)
            : TemporalPartitionColumns(granularity).Select(partition => partition.Name);

    private static string PartitionSelect(
        ISqlGenerationHelper sql,
        DuckDBTierPartitionColumn partition,
        string source)
        => $"{PartitionValue(partition.Transform, source)} AS {sql.DelimitIdentifier(partition.Name)}";

    private static string PartitionValue(TierPartitionTransform transform, string source)
        => transform switch
        {
            TierPartitionTransform.Value => source,
            TierPartitionTransform.Year => $"CAST(date_trunc('year', {source}) AS DATE)",
            TierPartitionTransform.Month => $"CAST(date_trunc('month', {source}) AS DATE)",
            TierPartitionTransform.Day => $"CAST({source} AS DATE)",
            _ => throw new ArgumentOutOfRangeException(nameof(transform), transform, null),
        };

    private static string AppendColumns(string columns, IEnumerable<string> additions)
    {
        var appended = additions.ToArray();
        return appended.Length == 0 ? columns : columns + ", " + string.Join(", ", appended);
    }

    private static string NormalizePath(string path) => path.TrimEnd('/', '\\');

    private static string TimestampLiteral(DateTime value)
        => $"TIMESTAMP {Literal(value.ToString(TimestampLiteralFormat, CultureInfo.InvariantCulture))}";

    private static string Literal(string value) => $"'{value.Replace("'", "''")}'";
}
