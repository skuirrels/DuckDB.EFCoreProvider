using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class TierControlTests
{
    private static readonly ISqlGenerationHelper Sql = GetSqlHelper();
    private static readonly string[] Columns = ["Id", "Ts", "Value"];

    [Theory]
    [InlineData(2024, 3, 17, 2024, 3, 1)]  // month granularity drops the day
    [InlineData(2024, 12, 31, 2024, 12, 1)]
    public void AlignCutoff_month_drops_to_first_of_month(int y, int m, int d, int ey, int em, int ed)
        => Assert.Equal(new DateTime(ey, em, ed), DuckDBTierControl.AlignCutoff(new DateTime(y, m, d, 9, 30, 0), TierGranularity.Month));

    [Fact]
    public void AlignCutoff_day_drops_time_of_day()
        => Assert.Equal(new DateTime(2024, 3, 17), DuckDBTierControl.AlignCutoff(new DateTime(2024, 3, 17, 9, 30, 0), TierGranularity.Day));

    [Fact]
    public void TierStorageCapability_preserves_existing_numeric_values()
    {
        Assert.Equal(0, (int)TierStorageCapability.Scheme);
        Assert.Equal(1, (int)TierStorageCapability.ExtensionInstalled);
        Assert.Equal(2, (int)TierStorageCapability.ExtensionLoaded);
        Assert.Equal(3, (int)TierStorageCapability.List);
        Assert.Equal(4, (int)TierStorageCapability.Read);
        Assert.Equal(5, (int)TierStorageCapability.Write);
        Assert.Equal(6, (int)TierStorageCapability.Delete);
        Assert.Equal(7, (int)TierStorageCapability.BindingOwnership);
    }

    [Fact]
    public void ViewSql_hot_only_selects_only_the_hot_table()
    {
        var sql = DuckDBTierControl.ViewSql(Sql, "events_tiered", "events", null, Columns, ["Id"], "Ts", "events_tiered", "archive/events", TierGranularity.Month, includeCold: false);

        Assert.Contains("CREATE OR REPLACE VIEW events_tiered AS", sql);
        Assert.Contains("FROM events AS h", sql);
        Assert.DoesNotContain("read_parquet", sql);
    }

    [Fact]
    public void ViewSql_full_unions_hot_and_cold_filtered_by_watermark()
    {
        var sql = DuckDBTierControl.ViewSql(Sql, "events_tiered", "events", null, Columns, ["Id"], "Ts", "events_tiered", "archive/events", TierGranularity.Month, includeCold: true);

        Assert.Contains("UNION ALL BY NAME", sql);
        // Cold branch uses SELECT * EXCLUDE so added columns are NULL-filled instead of erroring.
        Assert.Contains("SELECT * EXCLUDE (\"year\", \"month\")", sql);
        Assert.Contains("read_parquet('archive/events/**/*.parquet', hive_partitioning = true, union_by_name = true)", sql);
        Assert.Contains("WHERE (h.\"Ts\" IS NULL OR h.\"Ts\" >= (SELECT watermark FROM __duckdb_tier_control WHERE name = 'events_tiered'))", sql);
        Assert.Contains("AND NOT EXISTS (SELECT 1 FROM (SELECT * EXCLUDE (\"year\", \"month\") FROM read_parquet", sql);
        Assert.Contains("AS c WHERE c.\"Ts\" IS NOT NULL AND c.\"Ts\" < (SELECT watermark FROM __duckdb_tier_control WHERE name = 'events_tiered')", sql);
    }

    [Fact]
    public void ViewSql_schema_qualifies_the_hot_table()
    {
        var sql = DuckDBTierControl.ViewSql(Sql, "events_tiered", "events", "analytics", Columns, ["Id"], "Ts", "events_tiered", "archive/events", TierGranularity.Month, includeCold: false);

        Assert.Contains("FROM analytics.events AS h", sql);
    }

    [Fact]
    public void ViewSql_casts_hive_partition_values_back_to_their_mapped_store_types()
    {
        DuckDBTierPartitionColumn[] partitions =
        [
            new("GroupId", "INTEGER"),
            new("ValueBand", "DECIMAL(10,2)"),
            new("SnapshotAt", "TIMESTAMP"),
        ];
        var sql = DuckDBTierControl.ViewSql(
            Sql, "events_tiered", "events", null, [.. Columns, "GroupId", "ValueBand", "SnapshotAt"],
            ["Id"], "Ts", "events_tiered", "archive/events", TierGranularity.Month, includeCold: true,
            partitions);

        Assert.Contains(
            "SELECT * REPLACE ("
            + "CAST(\"GroupId\" AS INTEGER) AS \"GroupId\", "
            + "CAST(\"ValueBand\" AS DECIMAL(10,2)) AS \"ValueBand\", "
            + "CAST(\"SnapshotAt\" AS TIMESTAMP) AS \"SnapshotAt\")",
            sql);
        Assert.Contains(DuckDBTierPartitionContract.GetValidationColumn(partitions), sql);
    }

    [Fact]
    public void ArchiveCopySql_partitions_by_year_and_month_over_the_window()
    {
        var sql = DuckDBTierControl.ArchiveCopySql(
            Sql, "events", null, Columns, "Ts", "archive/events", TierGranularity.Month,
            new DateTime(2024, 1, 1), new DateTime(2024, 6, 1));

        Assert.Contains("year(\"Ts\") AS \"year\", month(\"Ts\") AS \"month\"", sql);
        Assert.Contains("PARTITION_BY (\"year\", \"month\"), OVERWRITE_OR_IGNORE", sql);
        Assert.Contains("\"Ts\" >= TIMESTAMP '2024-01-01", sql);
        Assert.Contains("\"Ts\" < TIMESTAMP '2024-06-01", sql);
    }

    [Fact]
    public void ArchiveCopySql_day_granularity_adds_day_partition()
    {
        var sql = DuckDBTierControl.ArchiveCopySql(
            Sql, "events", null, Columns, "Ts", "archive/events", TierGranularity.Day,
            new DateTime(2024, 1, 1), new DateTime(2024, 2, 1));

        Assert.Contains("day(\"Ts\") AS \"day\"", sql);
        Assert.Contains("PARTITION_BY (\"year\", \"month\", \"day\")", sql);
    }

    [Fact]
    public void ArchiveCopySql_uses_the_application_defined_partition_order_and_transform()
    {
        var sql = DuckDBTierControl.ArchiveCopySql(
            Sql, "events", null, [.. Columns, "GroupId"], "Ts", "archive/events", TierGranularity.Month,
            new DateTime(2024, 1, 1), new DateTime(2024, 6, 1),
            [
                new DuckDBTierPartitionColumn("GroupId", "GroupId", "GroupId", "INTEGER", TierPartitionTransform.Value),
                new DuckDBTierPartitionColumn("Ts", "Ts", "Ts_month", "DATE", TierPartitionTransform.Month),
            ]);

        Assert.Contains("CAST(date_trunc('month', \"Ts\") AS DATE) AS \"Ts_month\"", sql);
        Assert.Contains("PARTITION_BY (\"GroupId\", \"Ts_month\")", sql);
    }

    [Fact]
    public void ArchiveCopySql_projects_an_exact_partition_alias()
    {
        var sql = DuckDBTierControl.ArchiveCopySql(
            Sql, "events", null, [.. Columns, "GroupId"], "Ts", "archive/events", TierGranularity.Month,
            new DateTime(2024, 1, 1), new DateTime(2024, 6, 1),
            [
                new DuckDBTierPartitionColumn(
                    "GroupId", "GroupId", "root_group_id", "INTEGER", TierPartitionTransform.Value),
                new DuckDBTierPartitionColumn(
                    "Ts", "Ts", "effective_month", "DATE", TierPartitionTransform.Month),
            ]);

        Assert.Contains("\"GroupId\" AS root_group_id", sql);
        Assert.Contains("CAST(date_trunc('month', \"Ts\") AS DATE) AS effective_month", sql);
        Assert.Contains("PARTITION_BY (root_group_id, effective_month)", sql);
    }

    [Fact]
    public void Partition_definition_metadata_remains_backward_compatible_and_round_trips_aliases()
    {
        var legacy = Assert.Single(DuckDBTierPartitionDefinitionSerializer.Deserialize(
            """[{"PropertyName":"GroupId","Transform":"Value","IsImplicit":false}]"""));

        Assert.Null(legacy.PartitionName);
        Assert.Equal("customer_id", legacy.ResolveName("customer_id"));

        var json = DuckDBTierPartitionDefinitionSerializer.Serialize(
            [legacy with { PartitionName = "group_key" }]);
        var aliased = Assert.Single(DuckDBTierPartitionDefinitionSerializer.Deserialize(json));
        Assert.Equal("group_key", aliased.PartitionName);
        Assert.Equal("group_key", aliased.ResolveName("customer_id"));
    }

    [Fact]
    public void Literals_with_single_quotes_are_escaped()
    {
        var sql = DuckDBTierControl.ReadWatermarkSql(Sql, "it's");
        Assert.Contains("name = 'it''s'", sql);
    }

    [Fact]
    public void ReadGlob_is_recursive_parquet_glob()
        => Assert.Equal("archive/events/**/*.parquet", DuckDBTierControl.ReadGlob("archive/events/"));

    [Fact]
    public void Archive_file_probe_uses_existence_query_instead_of_counting_every_remote_match()
    {
        var sql = DuckDBArchiveFileProbe.ArchiveFileExistenceSql("s3://bucket/archive/events/");

        Assert.Equal("SELECT 1 FROM glob('s3://bucket/archive/events/**/*.parquet') LIMIT 1;", sql);
        Assert.DoesNotContain("count", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Archive_file_probe_lists_manifest_files_in_stable_order()
        => Assert.Equal(
            "SELECT file FROM glob('s3://bucket/archive/events/**/*.parquet') ORDER BY file;",
            DuckDBArchiveFileProbe.ArchiveFileListSql("s3://bucket/archive/events/"));

    [Fact]
    public void Archive_file_probe_describes_the_union_schema()
        => Assert.Equal(
            "DESCRIBE SELECT * FROM read_parquet('s3://bucket/archive/events/**/*.parquet', "
            + "hive_partitioning = true, union_by_name = true);",
            DuckDBArchiveFileProbe.ArchiveColumnListSql("s3://bucket/archive/events/"));

    [Theory]
    [InlineData("s3://bucket/archive/events", "s3://bucket/archive/events")]
    [InlineData("https://user:secret@example.test/archive?signature=secret", "https://example.test/archive")]
    public void Archive_manifest_paths_do_not_expose_url_credentials(string path, string expected)
        => Assert.Equal(expected, DuckDBTierArchiveManifest.RedactCredentials(path));

    [Fact]
    public void ArchiveChildCopySql_joins_to_the_root_for_the_boundary_and_partitions()
    {
        var chain = new[] { new DuckDBTierControl.TierJoinHop("RecordId", "records", null, "Id") };
        var sql = DuckDBTierControl.ArchiveChildCopySql(
            Sql, "record_lines", null, ["Id", "RecordId", "Value"], chain, "EffectiveAt",
            "archive/record_lines", TierGranularity.Month, new DateTime(2024, 1, 1), new DateTime(2024, 6, 1));

        Assert.Contains("FROM record_lines AS t0 JOIN records AS t1 ON t0.\"RecordId\" = t1.\"Id\"", sql);
        Assert.Contains("year(t1.\"EffectiveAt\") AS \"year\", month(t1.\"EffectiveAt\") AS \"month\"", sql);
        Assert.Contains("t1.\"EffectiveAt\" >= TIMESTAMP '2024-01-01", sql);
        Assert.Contains("t1.\"EffectiveAt\" < TIMESTAMP '2024-06-01", sql);
        Assert.Contains("TO 'archive/record_lines' (FORMAT PARQUET, PARTITION_BY (\"year\", \"month\"), OVERWRITE_OR_IGNORE)", sql);
    }

    [Fact]
    public void ArchiveChildCopySql_inherits_additional_partitions_from_the_root_join()
    {
        var chain = new[] { new DuckDBTierControl.TierJoinHop("RecordId", "records", null, "Id") };
        var sql = DuckDBTierControl.ArchiveChildCopySql(
            Sql, "record_lines", null, ["Id", "RecordId", "Value"], chain, "EffectiveAt",
            "archive/record_lines", TierGranularity.Month, new DateTime(2024, 1, 1),
            new DateTime(2024, 6, 1),
            [
                new DuckDBTierPartitionColumn("GroupId", "GroupId", "GroupId", "INTEGER", TierPartitionTransform.Value),
                new DuckDBTierPartitionColumn("EffectiveAt", "EffectiveAt", "EffectiveAt_month", "DATE", TierPartitionTransform.Month),
            ]);

        Assert.Contains("t1.\"GroupId\" AS \"GroupId\"", sql);
        Assert.Contains("CAST(date_trunc('month', t1.\"EffectiveAt\") AS DATE) AS \"EffectiveAt_month\"", sql);
        Assert.Contains("PARTITION_BY (\"GroupId\", \"EffectiveAt_month\")", sql);
    }

    [Fact]
    public void ChildViewSql_depth_one_semijoins_to_the_watermark_filtered_root()
    {
        var chain = new[] { new DuckDBTierControl.TierJoinHop("RecordId", "records", null, "Id") };
        var sql = DuckDBTierControl.ChildViewSql(
            Sql, "record_lines_tiered", "record_lines", null, ["Id", "RecordId", "Value"], ["Id"], chain, "EffectiveAt",
            "records", "archive/record_lines", TierGranularity.Month, includeCold: true, includeHotChildFilter: true);

        Assert.Contains("WHERE h.\"RecordId\" IN (SELECT r0.\"Id\" FROM records AS r0 WHERE (r0.\"EffectiveAt\" IS NULL OR r0.\"EffectiveAt\" >= (SELECT watermark FROM __duckdb_tier_control WHERE name = 'records'))", sql);
        Assert.Contains("OR NOT EXISTS (SELECT 1 FROM cold AS c WHERE c.\"Id\" = h.\"Id\")", sql);
        Assert.Contains("UNION ALL BY NAME", sql);
        Assert.Contains("SELECT * EXCLUDE (\"year\", \"month\")", sql);
    }

    [Fact]
    public void ChildViewSql_depth_two_chains_the_semijoin_up_to_the_root()
    {
        var chain = new[]
        {
            new DuckDBTierControl.TierJoinHop("RecordPartId", "record_lines", null, "Id"),
            new DuckDBTierControl.TierJoinHop("RecordId", "records", null, "Id"),
        };
        var sql = DuckDBTierControl.ChildViewSql(
            Sql, "line_allocations_tiered", "line_allocations", null, ["Id", "RecordPartId", "Value"], ["Id"], chain, "EffectiveAt",
            "records", "archive/line_allocations", TierGranularity.Month, includeCold: true, includeHotChildFilter: true);

        Assert.Contains(
            "WHERE h.\"RecordPartId\" IN (SELECT r0.\"Id\" FROM record_lines AS r0 WHERE r0.\"RecordId\" IN (SELECT r1.\"Id\" FROM records AS r1 WHERE (r1.\"EffectiveAt\" IS NULL OR r1.\"EffectiveAt\" >= (SELECT watermark FROM __duckdb_tier_control WHERE name = 'records'))))",
            sql);
    }

    [Fact]
    public void ChildViewSql_without_hot_filter_omits_the_semijoin()
    {
        var chain = new[] { new DuckDBTierControl.TierJoinHop("RecordId", "records", null, "Id") };
        var sql = DuckDBTierControl.ChildViewSql(
            Sql, "record_lines_tiered", "record_lines", null, ["Id", "RecordId", "Value"], ["Id"], chain, "EffectiveAt",
            "records", "archive/record_lines", TierGranularity.Month, includeCold: true, includeHotChildFilter: false);

        Assert.DoesNotContain("IN (SELECT", sql);
        Assert.Contains("WHERE NOT EXISTS (SELECT 1 FROM cold AS c WHERE c.\"Id\" = h.\"Id\")", sql);
    }

    [Fact]
    public void ChildViewSql_excludes_root_owned_partition_columns_from_the_child_shape()
    {
        var chain = new[] { new DuckDBTierControl.TierJoinHop("RecordId", "records", null, "Id") };
        var sql = DuckDBTierControl.ChildViewSql(
            Sql, "record_lines_tiered", "record_lines", null, ["Id", "RecordId", "Value"], ["Id"], chain,
            "EffectiveAt", "records", "archive/record_lines", TierGranularity.Month, includeCold: true,
            includeHotChildFilter: true,
            rootPartitions:
            [
                new DuckDBTierPartitionColumn("GroupId", "GroupId", "GroupId", "INTEGER", TierPartitionTransform.Value),
                new DuckDBTierPartitionColumn("EffectiveAt", "EffectiveAt", "EffectiveAt_month", "DATE", TierPartitionTransform.Month),
            ]);

        Assert.Contains("SELECT * EXCLUDE (\"GroupId\", \"EffectiveAt_month\")", sql);
    }

    [Fact]
    public void ChildViewSql_only_publishes_partitions_before_the_watermark()
    {
        var chain = new[] { new DuckDBTierControl.TierJoinHop("RecordId", "records", null, "Id") };
        var sql = DuckDBTierControl.ChildViewSql(
            Sql, "record_lines_tiered", "record_lines", null, ["Id", "RecordId", "Value"], ["Id"], chain,
            "EffectiveAt", "records", "archive/record_lines", TierGranularity.Month, includeCold: true,
            includeHotChildFilter: true,
            rootPartitions:
            [
                new DuckDBTierPartitionColumn(
                    "EffectiveAt", "EffectiveAt", "EffectiveAt_month", "DATE", TierPartitionTransform.Month),
            ]);

        Assert.Contains(
            "WHERE CAST(p.\"EffectiveAt_month\" AS DATE) < "
            + "(SELECT watermark FROM __duckdb_tier_control WHERE name = 'records')",
            sql);
    }

    [Fact]
    public void SharedChildViewSql_unions_each_root_archive_but_reads_the_hot_table_once()
    {
        var sql = DuckDBTierControl.SharedChildViewSql(
            Sql,
            "events_tiered",
            "events",
            null,
            ["Id", "RootAId", "RootBId"],
            ["Id"],
            [
                new DuckDBTierControl.TierChildViewBinding(
                    "binding-b",
                    [new DuckDBTierControl.TierJoinHop("RootBId", "root_b", null, "Id")],
                    "ArchivedAt",
                    "root-b",
                    "archive/b/events",
                    TierGranularity.Month,
                    IncludeCold: true,
                    IncludeHotChildFilter: true,
                    RootPartitions: [],
                    ArchiveFiles: null),
                new DuckDBTierControl.TierChildViewBinding(
                    "binding-a",
                    [new DuckDBTierControl.TierJoinHop("RootAId", "root_a", null, "Id")],
                    "ArchivedAt",
                    "root-a",
                    "archive/a/events",
                    TierGranularity.Month,
                    IncludeCold: true,
                    IncludeHotChildFilter: true,
                    RootPartitions: [],
                    ArchiveFiles: null),
            ]);

        Assert.Equal(1, sql.Split("FROM events AS h", StringSplitOptions.None).Length - 1);
        Assert.Contains("read_parquet('archive/a/events/**/*.parquet'", sql);
        Assert.Contains("read_parquet('archive/b/events/**/*.parquet'", sql);
        Assert.True(
            sql.IndexOf("archive/a/events", StringComparison.Ordinal)
            < sql.IndexOf("archive/b/events", StringComparison.Ordinal));
        Assert.Contains("h.\"RootAId\" IN (SELECT r0.\"Id\" FROM root_a AS r0", sql);
        Assert.Contains("h.\"RootBId\" IN (SELECT r0.\"Id\" FROM root_b AS r0", sql);
        Assert.Contains("OR (NOT EXISTS (SELECT 1 FROM cold AS c WHERE c.\"Id\" = h.\"Id\"))", sql);
    }

    [Fact]
    public void AmbiguousChildBindingCountSql_counts_rows_reachable_from_more_than_one_root()
    {
        var sql = DuckDBTierControl.AmbiguousChildBindingCountSql(
            Sql,
            "events",
            null,
            [
                new DuckDBTierControl.TierOwnershipBinding(
                    "binding-b",
                    [new DuckDBTierControl.TierJoinHop("RootBId", "root_b", null, "Id")]),
                new DuckDBTierControl.TierOwnershipBinding(
                    "binding-a",
                    [new DuckDBTierControl.TierJoinHop("RootAId", "root_a", null, "Id")]),
            ]);

        Assert.Contains("CASE WHEN EXISTS", sql);
        Assert.Contains("root_a", sql);
        Assert.Contains("root_b", sql);
        Assert.EndsWith("> 1;", sql);
    }

    [Fact]
    public void Match_key_sql_supports_composites_and_full_row_cleanup()
    {
        var chain = new[] { new DuckDBTierControl.TierJoinHop("RecordId", "records", null, "Id") };
        var sql = DuckDBTierControl.DeleteChildSql(
            Sql, "record_lines", null, ["RecordExternalKey", "PartCode"], ["Value"], chain,
            "EffectiveAt", "archive/record_lines", new DateTime(2024, 6, 1));

        Assert.Contains("c.\"RecordExternalKey\" = h.\"RecordExternalKey\"", sql);
        Assert.Contains("c.\"PartCode\" = h.\"PartCode\"", sql);
        Assert.Contains("c.\"Value\" IS NOT DISTINCT FROM h.\"Value\"", sql);
    }

    [Fact]
    public void Full_row_comparison_treats_a_missing_nullable_cold_column_as_null()
    {
        var sql = DuckDBTierControl.RootConflictCountSql(
            Sql,
            "events",
            null,
            ["ExternalId"],
            ["ExternalId", "Ts", "Note"],
            "Ts",
            "archive/events",
            new DateTime(2024, 6, 1),
            rootPartitions: null,
            archiveColumns: ["ExternalId", "Ts"]);

        Assert.Contains("c.\"Ts\" IS NOT DISTINCT FROM h.\"Ts\"", sql);
        Assert.Contains("h.\"Note\" IS NULL", sql);
        Assert.DoesNotContain("c.\"Note\"", sql);
    }

    [Fact]
    public void Control_table_ddl_upgrades_legacy_tables_with_a_partition_spec()
    {
        var sql = DuckDBTierControl.ControlTableDdl(Sql);

        Assert.Contains("partition_spec TEXT", sql);
        Assert.Contains("ADD COLUMN IF NOT EXISTS partition_spec TEXT", sql);
        Assert.Contains("archive_spec TEXT", sql);
        Assert.Contains("ADD COLUMN IF NOT EXISTS archive_spec TEXT", sql);
        Assert.Contains("active_archive_path TEXT", sql);
        Assert.Contains("ADD COLUMN IF NOT EXISTS active_archive_path TEXT", sql);
        Assert.Contains("archive_revision TEXT", sql);
        Assert.Contains("ADD COLUMN IF NOT EXISTS archive_revision TEXT", sql);
        Assert.Contains("bootstrap_from TIMESTAMP", sql);
        Assert.Contains("ADD COLUMN IF NOT EXISTS bootstrap_from TIMESTAMP", sql);
        Assert.Contains("bootstrap_to TIMESTAMP", sql);
        Assert.Contains("ADD COLUMN IF NOT EXISTS bootstrap_to TIMESTAMP", sql);
    }

    [Fact]
    public void Archive_publication_persists_the_active_immutable_generation()
    {
        var sql = DuckDBTierControl.PublishArchiveSql(
            Sql,
            "events",
            new DateTime(2024, 6, 1),
            "archive/events",
            "archive/_revisions/rev-1",
            "rev-1",
            TierGranularity.Month,
            "{\"Version\":1}");

        Assert.Contains("active_archive_path", sql);
        Assert.Contains("archive_revision", sql);
        Assert.Contains("'archive/_revisions/rev-1'", sql);
        Assert.Contains("'rev-1'", sql);
        Assert.Contains("active_archive_path = excluded.active_archive_path", sql);
    }

    [Fact]
    public void Reconcile_root_source_lets_hot_rows_win_by_stable_key()
    {
        var sql = DuckDBTierControl.ReconcileRootSourceSql(
            Sql,
            "events",
            null,
            Columns,
            ["ExternalId"],
            "Ts",
            "archive/events",
            TierGranularity.Month,
            new DateTime(2024, 6, 1),
            includeCold: true,
            rootPartitions: null);

        Assert.Contains("FROM events AS h", sql);
        Assert.Contains("UNION ALL BY NAME", sql);
        Assert.Contains("read_parquet('archive/events/**/*.parquet'", sql);
        Assert.Contains(
            "AND NOT EXISTS (SELECT 1 FROM events AS h WHERE c.\"ExternalId\" = h.\"ExternalId\")",
            sql);
    }

    [Fact]
    public void Partition_layout_is_persisted_without_advancing_the_watermark()
    {
        var sql = DuckDBTierControl.UpsertPartitionLayoutSql(
            Sql, "events", "archive/events", TierGranularity.Day, "{\"Version\":1}");

        Assert.Contains("(name, archive_path, granularity, partition_spec)", sql);
        Assert.DoesNotContain("watermark", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("granularity = excluded.granularity, partition_spec = excluded.partition_spec", sql);
    }

    [Fact]
    public void DeleteHotSql_casts_partitioned_key_before_matching_cold_rows()
    {
        var sql = DuckDBTierControl.DeleteHotSql(
            Sql, "events", null, ["GroupId"], "Ts", "archive/events", new DateTime(2024, 6, 1),
            [new DuckDBTierPartitionColumn("GroupId", "INTEGER")]);

        Assert.Contains(
            "FROM (SELECT * REPLACE (CAST(\"GroupId\" AS INTEGER) AS \"GroupId\") FROM read_parquet",
            sql);
    }

    [Fact]
    public void DeleteChildSql_matches_rows_whose_root_is_before_the_cutoff()
    {
        var chain = new[] { new DuckDBTierControl.TierJoinHop("RecordId", "records", null, "Id") };
        var sql = DuckDBTierControl.DeleteChildSql(Sql, "record_lines", null, ["Id"], chain, "EffectiveAt", "archive/record_lines", new DateTime(2024, 6, 1));

        Assert.Contains("DELETE FROM record_lines AS h WHERE h.\"RecordId\" IN (SELECT r0.\"Id\" FROM records AS r0 WHERE r0.\"EffectiveAt\" < TIMESTAMP '2024-06-01", sql);
        Assert.Contains("EXISTS (SELECT 1 FROM read_parquet('archive/record_lines/**/*.parquet'", sql);
    }

    private static ISqlGenerationHelper GetSqlHelper()
    {
        using var context = new HelperContext();
        return context.GetService<ISqlGenerationHelper>();
    }

    private sealed class HelperContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB("Data Source=:memory:");
    }
}
