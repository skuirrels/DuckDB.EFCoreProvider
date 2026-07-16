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
           + "name TEXT PRIMARY KEY, watermark TIMESTAMP, archive_path TEXT, granularity TEXT, partition_spec TEXT, "
           + "archive_spec TEXT, active_archive_path TEXT, archive_revision TEXT); "
           + $"ALTER TABLE {sql.DelimitIdentifier(ControlTable)} ADD COLUMN IF NOT EXISTS partition_spec TEXT; "
           + $"ALTER TABLE {sql.DelimitIdentifier(ControlTable)} ADD COLUMN IF NOT EXISTS archive_spec TEXT; "
           + $"ALTER TABLE {sql.DelimitIdentifier(ControlTable)} ADD COLUMN IF NOT EXISTS active_archive_path TEXT; "
           + $"ALTER TABLE {sql.DelimitIdentifier(ControlTable)} ADD COLUMN IF NOT EXISTS archive_revision TEXT;";

    /// <summary>Builds the scalar <c>SELECT</c> that reads a tiered entity's current watermark (or <c>NULL</c>).</summary>
    public static string ReadWatermarkSql(ISqlGenerationHelper sql, string controlKey)
        => $"SELECT watermark FROM {sql.DelimitIdentifier(ControlTable)} WHERE name = {Literal(controlKey)};";

    /// <summary>Builds the scalar <c>SELECT</c> that reads the persisted additional-partition layout signature.</summary>
    public static string ReadPartitionSpecSql(ISqlGenerationHelper sql, string controlKey)
        => $"SELECT partition_spec FROM {sql.DelimitIdentifier(ControlTable)} WHERE name = {Literal(controlKey)};";

    /// <summary>Builds the scalar <c>SELECT</c> that reads the persisted archive granularity.</summary>
    public static string ReadGranularitySql(ISqlGenerationHelper sql, string controlKey)
        => $"SELECT granularity FROM {sql.DelimitIdentifier(ControlTable)} WHERE name = {Literal(controlKey)};";

    /// <summary>Builds the scalar <c>SELECT</c> that reads the recorded archive path.</summary>
    public static string ReadArchivePathSql(ISqlGenerationHelper sql, string controlKey)
        => $"SELECT archive_path FROM {sql.DelimitIdentifier(ControlTable)} WHERE name = {Literal(controlKey)};";

    /// <summary>Builds the scalar <c>SELECT</c> that reads the versioned aggregate archive contract.</summary>
    public static string ReadArchiveSpecSql(ISqlGenerationHelper sql, string controlKey)
        => $"SELECT archive_spec FROM {sql.DelimitIdentifier(ControlTable)} WHERE name = {Literal(controlKey)};";

    /// <summary>Builds the scalar <c>SELECT</c> that reads the active archive generation base path.</summary>
    public static string ReadActiveArchivePathSql(ISqlGenerationHelper sql, string controlKey)
        => $"SELECT active_archive_path FROM {sql.DelimitIdentifier(ControlTable)} WHERE name = {Literal(controlKey)};";

    /// <summary>Builds the scalar <c>SELECT</c> that reads the active archive generation revision.</summary>
    public static string ReadArchiveRevisionSql(ISqlGenerationHelper sql, string controlKey)
        => $"SELECT archive_revision FROM {sql.DelimitIdentifier(ControlTable)} WHERE name = {Literal(controlKey)};";

    /// <summary>Persists the current aggregate archive contract without advancing the watermark.</summary>
    public static string UpsertArchiveSpecSql(
        ISqlGenerationHelper sql,
        string controlKey,
        string archivePath,
        string archiveSpec)
        => $"INSERT INTO {sql.DelimitIdentifier(ControlTable)} (name, archive_path, archive_spec) "
           + $"VALUES ({Literal(controlKey)}, {Literal(NormalizePath(archivePath))}, {Literal(archiveSpec)}) "
           + "ON CONFLICT (name) DO UPDATE SET archive_path = excluded.archive_path, archive_spec = excluded.archive_spec;";

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
    ///     Atomically publishes a watermark and the archive generation that generated views must read.
    /// </summary>
    public static string PublishArchiveSql(
        ISqlGenerationHelper sql,
        string controlKey,
        DateTime watermark,
        string configuredRootArchivePath,
        string activeArchiveBasePath,
        string? revision,
        TierGranularity granularity,
        string partitionSpec)
        => $"INSERT INTO {sql.DelimitIdentifier(ControlTable)} "
           + "(name, watermark, archive_path, active_archive_path, archive_revision, granularity, partition_spec) "
           + $"VALUES ({Literal(controlKey)}, {TimestampLiteral(watermark)}, {Literal(NormalizePath(configuredRootArchivePath))}, "
           + $"{Literal(NormalizePath(activeArchiveBasePath))}, "
           + $"{(revision is null ? "NULL" : Literal(revision))}, {Literal(granularity.ToString())}, {Literal(partitionSpec)}) "
           + "ON CONFLICT (name) DO UPDATE SET watermark = excluded.watermark, archive_path = excluded.archive_path, "
           + "active_archive_path = excluded.active_archive_path, archive_revision = excluded.archive_revision, "
           + "granularity = excluded.granularity, partition_spec = excluded.partition_spec;";

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

        var hotTimestamp = hotAlias + "." + ts;
        return builder
            .Append("SELECT ").Append(hotProjection).Append(" FROM ").Append(Table(sql, hotTable, hotSchema)).Append(" AS ").Append(hotAlias)
            .Append("\n  WHERE (").Append(hotTimestamp).Append(" IS NULL OR ").Append(hotTimestamp).Append(" >= ").Append(watermark).Append(')')
            .Append("\nUNION ALL BY NAME\n")
            .Append("SELECT ").Append(hotProjection).Append(" FROM ").Append(Table(sql, hotTable, hotSchema)).Append(" AS ").Append(hotAlias)
            .Append("\n  WHERE ").Append(hotTimestamp).Append(" IS NOT NULL AND ").Append(hotTimestamp).Append(" < ").Append(watermark)
            .Append(" AND NOT EXISTS (SELECT 1 FROM ").Append(coldSource).Append(" AS c WHERE c.").Append(ts)
            .Append(" IS NOT NULL AND c.").Append(ts).Append(" < ").Append(watermark).Append(" AND ").Append(hotKeyMatch).Append(')')
            .Append("\nUNION ALL BY NAME\n")
            .Append("SELECT * FROM ").Append(coldSource).Append(" AS c WHERE c.").Append(ts).Append(" IS NOT NULL AND c.").Append(ts).Append(" < ").Append(watermark).Append(';')
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
               + $"WHERE {ts} IS NOT NULL AND {ts} >= {TimestampLiteral(from)} AND {ts} < {TimestampLiteral(cutoff)}) "
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
        => DeleteHotSql(
            sql, hotTable, hotSchema, keyColumns, keyColumns, timestampColumn, archivePath, cutoff, rootPartitions);

    /// <summary>
    ///     Builds key-aware cleanup that deletes only a hot row whose complete archived representation matches.
    /// </summary>
    public static string DeleteHotSql(
        ISqlGenerationHelper sql,
        string hotTable,
        string? hotSchema,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<string> comparisonColumns,
        string timestampColumn,
        string archivePath,
        DateTime cutoff,
        IReadOnlyList<DuckDBTierPartitionColumn>? rootPartitions,
        IReadOnlyList<string>? archiveColumns = null)
    {
        const string hotAlias = "h";
        EnsureKeyColumns(keyColumns, hotTable);
        var ts = sql.DelimitIdentifier(timestampColumn);
        return $"DELETE FROM {Table(sql, hotTable, hotSchema)} AS {hotAlias} WHERE {hotAlias}.{ts} IS NOT NULL "
               + $"AND {hotAlias}.{ts} < {TimestampLiteral(cutoff)} "
               + $"AND EXISTS (SELECT 1 FROM {TypedParquetRead(sql, archivePath, rootPartitions)} AS c "
               + $"WHERE {KeyMatchPredicate(sql, keyColumns, "c", hotAlias)} AND c.{ts} IS NOT NULL "
               + $"AND c.{ts} < {TimestampLiteral(cutoff)} "
               + $"AND {RowMatchPredicate(sql, comparisonColumns, "c", hotAlias, archiveColumns)});";
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
        var (joins, rootAlias) = ChildRootJoins(sql, childTable, childSchema, chain);
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

        return $"COPY (SELECT {columnList}, {string.Join(", ", partitionSelect)} {joins} "
               + $"WHERE {rootTs} IS NOT NULL AND {rootTs} >= {TimestampLiteral(from)} AND {rootTs} < {TimestampLiteral(cutoff)}) "
               + $"TO {Literal(NormalizePath(archivePath))} "
               + $"(FORMAT PARQUET, PARTITION_BY ({partitionBy}), OVERWRITE_OR_IGNORE);";
    }

    /// <summary>Builds the complete replacement source for a published cold root generation.</summary>
    public static string ReconcileRootSourceSql(
        ISqlGenerationHelper sql,
        string hotTable,
        string? hotSchema,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> keyColumns,
        string timestampColumn,
        string activeArchivePath,
        TierGranularity granularity,
        DateTime watermark,
        bool includeCold,
        IReadOnlyList<DuckDBTierPartitionColumn>? rootPartitions)
    {
        const string hotAlias = "h";
        const string coldAlias = "c";
        var timestamp = sql.DelimitIdentifier(timestampColumn);
        var hot = $"SELECT {ColumnList(sql, columns, hotAlias)} FROM {Table(sql, hotTable, hotSchema)} AS {hotAlias} "
                  + $"WHERE {hotAlias}.{timestamp} IS NOT NULL AND {hotAlias}.{timestamp} < {TimestampLiteral(watermark)}";
        if (!includeCold)
        {
            return hot;
        }

        EnsureKeyColumns(keyColumns, hotTable);
        var coldProjection = StarProjection(
            sql,
            rootPartitions is { Count: > 0 }
                ? []
                : TemporalPartitionColumns(granularity).Select(partition => partition.Name),
            rootPartitions);
        var coldRead = $"(SELECT {coldProjection} FROM read_parquet({Literal(ReadGlob(activeArchivePath))}, "
                       + "hive_partitioning = true, union_by_name = true))";
        return hot
               + "\nUNION ALL BY NAME\n"
               + $"SELECT * FROM {coldRead} AS {coldAlias} "
               + $"WHERE {coldAlias}.{timestamp} IS NOT NULL AND {coldAlias}.{timestamp} < {TimestampLiteral(watermark)} "
               + $"AND NOT EXISTS (SELECT 1 FROM {Table(sql, hotTable, hotSchema)} AS {hotAlias} "
               + $"WHERE {KeyMatchPredicate(sql, keyColumns, coldAlias, hotAlias)})";
    }

    /// <summary>Builds the complete replacement source for a published cold child generation.</summary>
    public static string ReconcileChildSourceSql(
        ISqlGenerationHelper sql,
        string childTable,
        string? childSchema,
        IReadOnlyList<string> childColumns,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<TierJoinHop> chain,
        string rootTimestampColumn,
        string activeArchivePath,
        TierGranularity granularity,
        DateTime watermark,
        bool includeCold,
        IReadOnlyList<DuckDBTierPartitionColumn>? rootPartitions)
    {
        const string hotAlias = "h";
        const string coldAlias = "c";
        var (joins, rootAlias) = ChildRootJoins(sql, childTable, childSchema, chain);
        var rootTimestamp = $"{rootAlias}.{sql.DelimitIdentifier(rootTimestampColumn)}";
        var partitionSelect = rootPartitions is { Count: > 0 }
            ? rootPartitions.Select(partition => PartitionSelect(
                sql,
                partition,
                $"{rootAlias}.{sql.DelimitIdentifier(partition.SourceColumn)}"))
            : TemporalPartitionColumns(granularity)
                .Select(partition => $"{partition.Function}({rootTimestamp}) AS {sql.DelimitIdentifier(partition.Name)}");
        var hotProjection = AppendColumns(
            string.Join(", ", childColumns.Select(column => $"t0.{sql.DelimitIdentifier(column)}")),
            partitionSelect);
        var hot = $"SELECT {hotProjection} {joins} WHERE {rootTimestamp} IS NOT NULL "
                  + $"AND {rootTimestamp} < {TimestampLiteral(watermark)}";
        if (!includeCold)
        {
            return hot;
        }

        EnsureKeyColumns(keyColumns, childTable);
        var coldProjection = rootPartitions is { Count: > 0 }
            ? StarProjection(sql, [], rootPartitions)
            : "*";
        var publication = ArchivePartitionRangePredicate(
            sql, rootTimestampColumn, granularity, DateTime.MinValue, watermark, rootPartitions, coldAlias);
        return hot
               + "\nUNION ALL BY NAME\n"
               + $"SELECT {coldProjection} FROM read_parquet({Literal(ReadGlob(activeArchivePath))}, "
               + $"hive_partitioning = true, union_by_name = true) AS {coldAlias} "
               + $"WHERE {publication} AND NOT EXISTS (SELECT 1 FROM {Table(sql, childTable, childSchema)} AS {hotAlias} "
               + $"WHERE {KeyMatchPredicate(sql, keyColumns, coldAlias, hotAlias)})";
    }

    /// <summary>Builds a partitioned <c>COPY</c> from a complete reconciled root source.</summary>
    public static string ReconcileRootCopySql(
        ISqlGenerationHelper sql,
        string sourceSql,
        IReadOnlyList<string> columns,
        string timestampColumn,
        string archivePath,
        TierGranularity granularity,
        IReadOnlyList<DuckDBTierPartitionColumn>? rootPartitions)
    {
        const string alias = "s";
        var columnList = string.Join(", ", columns.Select(column => $"{alias}.{sql.DelimitIdentifier(column)}"));
        var partitionSelect = rootPartitions is { Count: > 0 }
            ? rootPartitions.Where(partition => partition.Transform != TierPartitionTransform.Value)
                .Select(partition => PartitionSelect(
                    sql,
                    partition,
                    $"{alias}.{sql.DelimitIdentifier(partition.SourceColumn)}"))
            : TemporalPartitionColumns(granularity)
                .Select(partition =>
                    $"{partition.Function}({alias}.{sql.DelimitIdentifier(timestampColumn)}) AS {sql.DelimitIdentifier(partition.Name)}");
        var copyProjection = AppendColumns(columnList, partitionSelect);
        var partitionBy = string.Join(", ", PartitionColumns(granularity, rootPartitions).Select(sql.DelimitIdentifier));
        return $"COPY (WITH source AS ({sourceSql}) SELECT {copyProjection} FROM source AS {alias}) "
               + $"TO {Literal(NormalizePath(archivePath))} "
               + $"(FORMAT PARQUET, PARTITION_BY ({partitionBy}), OVERWRITE_OR_IGNORE);";
    }

    /// <summary>Builds a partitioned <c>COPY</c> from a complete reconciled child source.</summary>
    public static string ReconcileChildCopySql(
        ISqlGenerationHelper sql,
        string sourceSql,
        string archivePath,
        TierGranularity granularity,
        IReadOnlyList<DuckDBTierPartitionColumn>? rootPartitions)
    {
        var partitionBy = string.Join(", ", PartitionColumns(granularity, rootPartitions).Select(sql.DelimitIdentifier));
        return $"COPY (WITH source AS ({sourceSql}) SELECT * FROM source) "
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
                + " IN (" + RootSemijoin(sql, chain, rootTimestampColumn, 0, watermark, includeNullRoot: true) + ") OR ");
        }

        var coldPublicationPredicate = ChildColdPublicationPredicate(
            sql, rootTimestampColumn, controlKey, granularity, rootPartitions);

        return viewHeader
            .Append("WITH cold AS (\n")
            .Append("SELECT * EXCLUDE (").Append(excludeList).Append(')')
            .Append("\n  FROM read_parquet(").Append(Literal(ReadGlob(archivePath)))
            .Append(", hive_partitioning = true, union_by_name = true)")
            .Append(" AS p\n  WHERE ").Append(coldPublicationPredicate)
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
        => DeleteChildSql(
            sql, childTable, childSchema, childKeyColumns, childKeyColumns, chain, rootTimestampColumn,
            archivePath, cutoff);

    /// <summary>Builds child cleanup that requires the complete archived representation to match.</summary>
    public static string DeleteChildSql(
        ISqlGenerationHelper sql,
        string childTable,
        string? childSchema,
        IReadOnlyList<string> childKeyColumns,
        IReadOnlyList<string> comparisonColumns,
        IReadOnlyList<TierJoinHop> chain,
        string rootTimestampColumn,
        string archivePath,
        DateTime cutoff,
        IReadOnlyList<string>? archiveColumns = null)
    {
        const string childAlias = "h";
        EnsureKeyColumns(childKeyColumns, childTable);
        return $"DELETE FROM {Table(sql, childTable, childSchema)} AS {childAlias} WHERE {childAlias}.{sql.DelimitIdentifier(chain[0].ForeignKeyColumn)} "
               + $"IN ({RootSemijoin(sql, chain, rootTimestampColumn, 0, "< " + TimestampLiteral(cutoff))}) "
               + $"AND EXISTS (SELECT 1 FROM read_parquet({Literal(ReadGlob(archivePath))}, hive_partitioning = true, union_by_name = true) AS c "
               + $"WHERE {KeyMatchPredicate(sql, childKeyColumns, "c", childAlias)} "
               + $"AND {RowMatchPredicate(sql, comparisonColumns, "c", childAlias, archiveColumns)});";
    }

    // Nested semijoin from the child's foreign key up the chain to the root, ending in "<rootTs> <rootCondition>".
    private static string RootSemijoin(
        ISqlGenerationHelper sql,
        IReadOnlyList<TierJoinHop> chain,
        string rootTimestampColumn,
        int level,
        string rootCondition,
        bool includeNullRoot = false)
    {
        var hop = chain[level];
        var alias = "r" + level.ToString(CultureInfo.InvariantCulture);
        var select = $"SELECT {alias}.{sql.DelimitIdentifier(hop.PrincipalKeyColumn)} FROM {Table(sql, hop.PrincipalTable, hop.PrincipalSchema)} AS {alias} WHERE ";
        if (level == chain.Count - 1)
        {
            var rootTimestamp = $"{alias}.{sql.DelimitIdentifier(rootTimestampColumn)}";
            return select + (includeNullRoot
                ? $"({rootTimestamp} IS NULL OR {rootTimestamp} {rootCondition})"
                : $"{rootTimestamp} {rootCondition}");
        }

        return select + $"{alias}.{sql.DelimitIdentifier(chain[level + 1].ForeignKeyColumn)} IN "
            + $"({RootSemijoin(sql, chain, rootTimestampColumn, level + 1, rootCondition, includeNullRoot)})";
    }

    /// <summary>Counts selected archive rows that contain a null configured match-key component.</summary>
    public static string NullMatchKeyCountSql(
        ISqlGenerationHelper sql,
        string table,
        string? schema,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<TierJoinHop> chain,
        string rootTimestampColumn,
        DateTime from,
        DateTime cutoff)
    {
        const string alias = "t0";
        var nullKey = string.Join(
            " OR ",
            keyColumns.Select(column => $"{alias}.{sql.DelimitIdentifier(column)} IS NULL"));
        if (chain.Count == 0)
        {
            var timestamp = $"{alias}.{sql.DelimitIdentifier(rootTimestampColumn)}";
            return $"SELECT count(*) FROM {Table(sql, table, schema)} AS {alias} WHERE {timestamp} IS NOT NULL "
                   + $"AND {timestamp} >= {TimestampLiteral(from)} AND {timestamp} < {TimestampLiteral(cutoff)} "
                   + $"AND ({nullKey});";
        }

        var (joins, rootAlias) = ChildRootJoins(sql, table, schema, chain);
        var rootTimestamp = $"{rootAlias}.{sql.DelimitIdentifier(rootTimestampColumn)}";
        return $"SELECT count(*) {joins} WHERE {rootTimestamp} IS NOT NULL "
               + $"AND {rootTimestamp} >= {TimestampLiteral(from)} AND {rootTimestamp} < {TimestampLiteral(cutoff)} "
               + $"AND ({nullKey});";
    }

    /// <summary>Counts hot rows selected for a root-owned archive window.</summary>
    public static string HotWindowCountSql(
        ISqlGenerationHelper sql,
        string table,
        string? schema,
        IReadOnlyList<TierJoinHop> chain,
        string rootTimestampColumn,
        DateTime from,
        DateTime cutoff)
    {
        if (chain.Count == 0)
        {
            var timestamp = sql.DelimitIdentifier(rootTimestampColumn);
            return $"SELECT count(*) FROM {Table(sql, table, schema)} WHERE {timestamp} IS NOT NULL "
                   + $"AND {timestamp} >= {TimestampLiteral(from)} AND {timestamp} < {TimestampLiteral(cutoff)};";
        }

        var (joins, rootAlias) = ChildRootJoins(sql, table, schema, chain);
        var rootTimestamp = $"{rootAlias}.{sql.DelimitIdentifier(rootTimestampColumn)}";
        return $"SELECT count(*) {joins} WHERE {rootTimestamp} IS NOT NULL "
               + $"AND {rootTimestamp} >= {TimestampLiteral(from)} AND {rootTimestamp} < {TimestampLiteral(cutoff)};";
    }

    /// <summary>Counts every row in a physical hot table.</summary>
    public static string HotTableCountSql(ISqlGenerationHelper sql, string table, string? schema)
        => $"SELECT count(*) FROM {Table(sql, table, schema)};";

    /// <summary>Counts every Parquet row beneath an archive table path.</summary>
    public static string ArchiveRowCountSql(string archivePath)
        => $"SELECT count(*) FROM read_parquet({Literal(ReadGlob(archivePath))}, "
           + "hive_partitioning = true, union_by_name = true);";

    /// <summary>Counts the rows visible in one archive window for manifest verification.</summary>
    public static string ArchiveWindowCountSql(
        ISqlGenerationHelper sql,
        string archivePath,
        bool isRoot,
        string rootTimestampColumn,
        TierGranularity granularity,
        DateTime from,
        DateTime cutoff,
        IReadOnlyList<DuckDBTierPartitionColumn>? rootPartitions)
    {
        const string alias = "p";
        if (isRoot)
        {
            var timestamp = $"{alias}.{sql.DelimitIdentifier(rootTimestampColumn)}";
            return $"SELECT count(*) FROM {TypedParquetRead(sql, archivePath, rootPartitions)} AS {alias} "
                   + $"WHERE {timestamp} IS NOT NULL AND {timestamp} >= {TimestampLiteral(from)} "
                   + $"AND {timestamp} < {TimestampLiteral(cutoff)};";
        }

        return $"SELECT count(*) FROM read_parquet({Literal(ReadGlob(archivePath))}, "
               + $"hive_partitioning = true, union_by_name = true) AS {alias} WHERE "
               + ArchivePartitionRangePredicate(
                   sql, rootTimestampColumn, granularity, from, cutoff, rootPartitions, alias)
               + ";";
    }

    /// <summary>Counts a complete reconciliation source without writing it.</summary>
    public static string ReconcileSourceCountSql(string sourceSql)
        => $"SELECT count(*) FROM ({sourceSql}) AS source;";

    /// <summary>
    ///     Counts archived roots whose hot representation changes the lifecycle value. Reconciliation rejects
    ///     these rows because moving an aggregate between hot and cold requires restoring its whole graph.
    /// </summary>
    public static string RootLifecycleChangeCountSql(
        ISqlGenerationHelper sql,
        string table,
        string? schema,
        IReadOnlyList<string> keyColumns,
        string timestampColumn,
        string archivePath,
        DateTime watermark,
        IReadOnlyList<DuckDBTierPartitionColumn>? rootPartitions)
    {
        const string hotAlias = "h";
        const string coldAlias = "c";
        var timestamp = sql.DelimitIdentifier(timestampColumn);
        return $"SELECT count(*) FROM {Table(sql, table, schema)} AS {hotAlias} "
               + $"JOIN {TypedParquetRead(sql, archivePath, rootPartitions)} AS {coldAlias} "
               + $"ON {KeyMatchPredicate(sql, keyColumns, coldAlias, hotAlias)} "
               + $"WHERE {coldAlias}.{timestamp} IS NOT NULL AND {coldAlias}.{timestamp} < {TimestampLiteral(watermark)} "
               + $"AND {hotAlias}.{timestamp} IS DISTINCT FROM {coldAlias}.{timestamp};";
    }

    /// <summary>Counts differing hot root representations whose configured match key already exists in cold data.</summary>
    public static string RootConflictCountSql(
        ISqlGenerationHelper sql,
        string table,
        string? schema,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<string> comparisonColumns,
        string timestampColumn,
        string archivePath,
        DateTime watermark,
        IReadOnlyList<DuckDBTierPartitionColumn>? rootPartitions,
        IReadOnlyList<string>? archiveColumns = null)
    {
        const string hotAlias = "h";
        const string coldAlias = "c";
        var timestamp = $"{coldAlias}.{sql.DelimitIdentifier(timestampColumn)}";
        return $"SELECT count(*) FROM {Table(sql, table, schema)} AS {hotAlias} "
               + $"JOIN {TypedParquetRead(sql, archivePath, rootPartitions)} AS {coldAlias} "
               + $"ON {KeyMatchPredicate(sql, keyColumns, coldAlias, hotAlias)} "
               + $"WHERE {timestamp} IS NOT NULL AND {timestamp} < {TimestampLiteral(watermark)} "
               + $"AND NOT ({RowMatchPredicate(sql, comparisonColumns, coldAlias, hotAlias, archiveColumns)});";
    }

    /// <summary>Counts differing hot child representations whose configured match key already exists in published cold data.</summary>
    public static string ChildConflictCountSql(
        ISqlGenerationHelper sql,
        string table,
        string? schema,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<string> comparisonColumns,
        string rootTimestampColumn,
        string controlKey,
        string archivePath,
        TierGranularity granularity,
        IReadOnlyList<DuckDBTierPartitionColumn>? rootPartitions,
        IReadOnlyList<string>? archiveColumns = null)
    {
        const string hotAlias = "h";
        const string coldAlias = "c";
        var excludeList = string.Join(", ",
            PartitionColumns(granularity, rootPartitions).Select(sql.DelimitIdentifier));
        var published = ChildColdPublicationPredicate(
            sql, rootTimestampColumn, controlKey, granularity, rootPartitions);
        return $"WITH cold AS (SELECT * EXCLUDE ({excludeList}) "
               + $"FROM read_parquet({Literal(ReadGlob(archivePath))}, hive_partitioning = true, union_by_name = true) AS p "
               + $"WHERE {published}) "
               + $"SELECT count(*) FROM {Table(sql, table, schema)} AS {hotAlias} JOIN cold AS {coldAlias} "
               + $"ON {KeyMatchPredicate(sql, keyColumns, coldAlias, hotAlias)} "
               + $"WHERE NOT ({RowMatchPredicate(sql, comparisonColumns, coldAlias, hotAlias, archiveColumns)});";
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

    private static string RowMatchPredicate(
        ISqlGenerationHelper sql,
        IReadOnlyList<string> columns,
        string leftAlias,
        string rightAlias,
        IReadOnlyList<string>? leftColumns = null)
    {
        var available = leftColumns?.ToHashSet(StringComparer.Ordinal);
        return string.Join(
            " AND ",
            columns.Select(column => available is null || available.Contains(column)
                ? $"{leftAlias}.{sql.DelimitIdentifier(column)} IS NOT DISTINCT FROM {rightAlias}.{sql.DelimitIdentifier(column)}"
                : $"{rightAlias}.{sql.DelimitIdentifier(column)} IS NULL"));
    }

    private static string ChildColdPublicationPredicate(
        ISqlGenerationHelper sql,
        string rootTimestampColumn,
        string controlKey,
        TierGranularity granularity,
        IReadOnlyList<DuckDBTierPartitionColumn>? rootPartitions)
    {
        var watermark = $"(SELECT watermark FROM {sql.DelimitIdentifier(ControlTable)} WHERE name = {Literal(controlKey)})";
        if (rootPartitions is { Count: > 0 })
        {
            var lifecycle = rootPartitions.Single(partition =>
                partition.SourceColumn == rootTimestampColumn
                && partition.Transform is TierPartitionTransform.Value or TierPartitionTransform.Month or TierPartitionTransform.Day);
            var partitionColumn = $"p.{sql.DelimitIdentifier(lifecycle.Name)}";
            var typedPartition = lifecycle.Transform == TierPartitionTransform.Value
                ? $"CAST({partitionColumn} AS {lifecycle.StoreType})"
                : $"CAST({partitionColumn} AS DATE)";
            return $"{typedPartition} < {watermark}";
        }

        var year = $"CAST(p.{sql.DelimitIdentifier("year")} AS INTEGER)";
        var month = $"CAST(p.{sql.DelimitIdentifier("month")} AS INTEGER)";
        var day = granularity == TierGranularity.Day
            ? $"CAST(p.{sql.DelimitIdentifier("day")} AS INTEGER)"
            : "1";
        return $"make_date({year}, {month}, {day}) < CAST({watermark} AS DATE)";
    }

    private static string ArchivePartitionRangePredicate(
        ISqlGenerationHelper sql,
        string rootTimestampColumn,
        TierGranularity granularity,
        DateTime from,
        DateTime cutoff,
        IReadOnlyList<DuckDBTierPartitionColumn>? rootPartitions,
        string alias)
    {
        string value;
        if (rootPartitions is { Count: > 0 })
        {
            var lifecycle = rootPartitions.Single(partition =>
                partition.SourceColumn == rootTimestampColumn
                && partition.Transform is TierPartitionTransform.Value or TierPartitionTransform.Month or TierPartitionTransform.Day);
            value = $"CAST({alias}.{sql.DelimitIdentifier(lifecycle.Name)} AS TIMESTAMP)";
        }
        else
        {
            var year = $"CAST({alias}.{sql.DelimitIdentifier("year")} AS INTEGER)";
            var month = $"CAST({alias}.{sql.DelimitIdentifier("month")} AS INTEGER)";
            var day = granularity == TierGranularity.Day
                ? $"CAST({alias}.{sql.DelimitIdentifier("day")} AS INTEGER)"
                : "1";
            value = $"CAST(make_date({year}, {month}, {day}) AS TIMESTAMP)";
        }

        return $"{value} >= {TimestampLiteral(from)} AND {value} < {TimestampLiteral(cutoff)}";
    }

    private static (string Joins, string RootAlias) ChildRootJoins(
        ISqlGenerationHelper sql,
        string childTable,
        string? childSchema,
        IReadOnlyList<TierJoinHop> chain)
    {
        var joins = new StringBuilder("FROM ").Append(Table(sql, childTable, childSchema)).Append(" AS t0");
        for (var i = 0; i < chain.Count; i++)
        {
            joins.Append(" JOIN ").Append(Table(sql, chain[i].PrincipalTable, chain[i].PrincipalSchema)).Append(" AS t").Append(i + 1)
                .Append(" ON t").Append(i).Append('.').Append(sql.DelimitIdentifier(chain[i].ForeignKeyColumn))
                .Append(" = t").Append(i + 1).Append('.').Append(sql.DelimitIdentifier(chain[i].PrincipalKeyColumn));
        }

        return (joins.ToString(), "t" + chain.Count.ToString(CultureInfo.InvariantCulture));
    }

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
