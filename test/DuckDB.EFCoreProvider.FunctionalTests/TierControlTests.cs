using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class TierControlTests
{
    private static readonly ISqlGenerationHelper Sql = GetSqlHelper();
    private static readonly string[] Columns = ["Id", "Ts", "Amount"];

    [Theory]
    [InlineData(2024, 3, 17, 2024, 3, 1)]  // month granularity drops the day
    [InlineData(2024, 12, 31, 2024, 12, 1)]
    public void AlignCutoff_month_drops_to_first_of_month(int y, int m, int d, int ey, int em, int ed)
        => Assert.Equal(new DateTime(ey, em, ed), DuckDBTierControl.AlignCutoff(new DateTime(y, m, d, 9, 30, 0), TierGranularity.Month));

    [Fact]
    public void AlignCutoff_day_drops_time_of_day()
        => Assert.Equal(new DateTime(2024, 3, 17), DuckDBTierControl.AlignCutoff(new DateTime(2024, 3, 17, 9, 30, 0), TierGranularity.Day));

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
        Assert.Contains("WHERE h.\"Ts\" >= (SELECT watermark FROM __duckdb_tier_control WHERE name = 'events_tiered')", sql);
        Assert.Contains("AND NOT EXISTS (SELECT 1 FROM (SELECT * EXCLUDE (\"year\", \"month\") FROM read_parquet", sql);
        Assert.Contains("AS c WHERE c.\"Ts\" < (SELECT watermark FROM __duckdb_tier_control WHERE name = 'events_tiered')", sql);
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
        var sql = DuckDBTierControl.ViewSql(
            Sql, "events_tiered", "events", null, [.. Columns, "CustomerId", "AmountBand", "SnapshotAt"],
            ["Id"], "Ts", "events_tiered", "archive/events", TierGranularity.Month, includeCold: true,
            [
                new DuckDBTierPartitionColumn("CustomerId", "INTEGER"),
                new DuckDBTierPartitionColumn("AmountBand", "DECIMAL(10,2)"),
                new DuckDBTierPartitionColumn("SnapshotAt", "TIMESTAMP"),
            ]);

        Assert.Contains(
            "SELECT * REPLACE ("
            + "CAST(\"CustomerId\" AS INTEGER) AS \"CustomerId\", "
            + "CAST(\"AmountBand\" AS DECIMAL(10,2)) AS \"AmountBand\", "
            + "CAST(\"SnapshotAt\" AS TIMESTAMP) AS \"SnapshotAt\")",
            sql);
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
            Sql, "events", null, [.. Columns, "CustomerId"], "Ts", "archive/events", TierGranularity.Month,
            new DateTime(2024, 1, 1), new DateTime(2024, 6, 1),
            [
                new DuckDBTierPartitionColumn("CustomerId", "CustomerId", "CustomerId", "INTEGER", TierPartitionTransform.Value),
                new DuckDBTierPartitionColumn("Ts", "Ts", "Ts_month", "DATE", TierPartitionTransform.Month),
            ]);

        Assert.Contains("CAST(date_trunc('month', \"Ts\") AS DATE) AS \"Ts_month\"", sql);
        Assert.Contains("PARTITION_BY (\"CustomerId\", \"Ts_month\")", sql);
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
    public void ArchiveChildCopySql_joins_to_the_root_for_the_boundary_and_partitions()
    {
        var chain = new[] { new DuckDBTierControl.TierJoinHop("InvoiceId", "invoices", null, "Id") };
        var sql = DuckDBTierControl.ArchiveChildCopySql(
            Sql, "invoice_lines", null, ["Id", "InvoiceId", "Amount"], chain, "InvoiceDate",
            "archive/invoice_lines", TierGranularity.Month, new DateTime(2024, 1, 1), new DateTime(2024, 6, 1));

        Assert.Contains("FROM invoice_lines AS t0 JOIN invoices AS t1 ON t0.\"InvoiceId\" = t1.\"Id\"", sql);
        Assert.Contains("year(t1.\"InvoiceDate\") AS \"year\", month(t1.\"InvoiceDate\") AS \"month\"", sql);
        Assert.Contains("t1.\"InvoiceDate\" >= TIMESTAMP '2024-01-01", sql);
        Assert.Contains("t1.\"InvoiceDate\" < TIMESTAMP '2024-06-01", sql);
        Assert.Contains("TO 'archive/invoice_lines' (FORMAT PARQUET, PARTITION_BY (\"year\", \"month\"), OVERWRITE_OR_IGNORE)", sql);
    }

    [Fact]
    public void ArchiveChildCopySql_inherits_additional_partitions_from_the_root_join()
    {
        var chain = new[] { new DuckDBTierControl.TierJoinHop("InvoiceId", "invoices", null, "Id") };
        var sql = DuckDBTierControl.ArchiveChildCopySql(
            Sql, "invoice_lines", null, ["Id", "InvoiceId", "Amount"], chain, "InvoiceDate",
            "archive/invoice_lines", TierGranularity.Month, new DateTime(2024, 1, 1),
            new DateTime(2024, 6, 1),
            [
                new DuckDBTierPartitionColumn("CustomerId", "CustomerId", "CustomerId", "INTEGER", TierPartitionTransform.Value),
                new DuckDBTierPartitionColumn("InvoiceDate", "InvoiceDate", "InvoiceDate_month", "DATE", TierPartitionTransform.Month),
            ]);

        Assert.Contains("t1.\"CustomerId\" AS \"CustomerId\"", sql);
        Assert.Contains("CAST(date_trunc('month', t1.\"InvoiceDate\") AS DATE) AS \"InvoiceDate_month\"", sql);
        Assert.Contains("PARTITION_BY (\"CustomerId\", \"InvoiceDate_month\")", sql);
    }

    [Fact]
    public void ChildViewSql_depth_one_semijoins_to_the_watermark_filtered_root()
    {
        var chain = new[] { new DuckDBTierControl.TierJoinHop("InvoiceId", "invoices", null, "Id") };
        var sql = DuckDBTierControl.ChildViewSql(
            Sql, "invoice_lines_tiered", "invoice_lines", null, ["Id", "InvoiceId", "Amount"], ["Id"], chain, "InvoiceDate",
            "invoices", "archive/invoice_lines", TierGranularity.Month, includeCold: true, includeHotChildFilter: true);

        Assert.Contains("WHERE h.\"InvoiceId\" IN (SELECT r0.\"Id\" FROM invoices AS r0 WHERE r0.\"InvoiceDate\" >= (SELECT watermark FROM __duckdb_tier_control WHERE name = 'invoices')", sql);
        Assert.Contains("OR NOT EXISTS (SELECT 1 FROM cold AS c WHERE c.\"Id\" = h.\"Id\")", sql);
        Assert.Contains("UNION ALL BY NAME", sql);
        Assert.Contains("SELECT * EXCLUDE (\"year\", \"month\")", sql);
    }

    [Fact]
    public void ChildViewSql_depth_two_chains_the_semijoin_up_to_the_root()
    {
        var chain = new[]
        {
            new DuckDBTierControl.TierJoinHop("InvoiceLineId", "invoice_lines", null, "Id"),
            new DuckDBTierControl.TierJoinHop("InvoiceId", "invoices", null, "Id"),
        };
        var sql = DuckDBTierControl.ChildViewSql(
            Sql, "line_allocations_tiered", "line_allocations", null, ["Id", "InvoiceLineId", "Amount"], ["Id"], chain, "InvoiceDate",
            "invoices", "archive/line_allocations", TierGranularity.Month, includeCold: true, includeHotChildFilter: true);

        Assert.Contains(
            "WHERE h.\"InvoiceLineId\" IN (SELECT r0.\"Id\" FROM invoice_lines AS r0 WHERE r0.\"InvoiceId\" IN (SELECT r1.\"Id\" FROM invoices AS r1 WHERE r1.\"InvoiceDate\" >= (SELECT watermark FROM __duckdb_tier_control WHERE name = 'invoices')))",
            sql);
    }

    [Fact]
    public void ChildViewSql_without_hot_filter_omits_the_semijoin()
    {
        var chain = new[] { new DuckDBTierControl.TierJoinHop("InvoiceId", "invoices", null, "Id") };
        var sql = DuckDBTierControl.ChildViewSql(
            Sql, "invoice_lines_tiered", "invoice_lines", null, ["Id", "InvoiceId", "Amount"], ["Id"], chain, "InvoiceDate",
            "invoices", "archive/invoice_lines", TierGranularity.Month, includeCold: true, includeHotChildFilter: false);

        Assert.DoesNotContain("IN (SELECT", sql);
        Assert.Contains("WHERE NOT EXISTS (SELECT 1 FROM cold AS c WHERE c.\"Id\" = h.\"Id\")", sql);
    }

    [Fact]
    public void ChildViewSql_excludes_root_owned_partition_columns_from_the_child_shape()
    {
        var chain = new[] { new DuckDBTierControl.TierJoinHop("InvoiceId", "invoices", null, "Id") };
        var sql = DuckDBTierControl.ChildViewSql(
            Sql, "invoice_lines_tiered", "invoice_lines", null, ["Id", "InvoiceId", "Amount"], ["Id"], chain,
            "InvoiceDate", "invoices", "archive/invoice_lines", TierGranularity.Month, includeCold: true,
            includeHotChildFilter: true,
            rootPartitions:
            [
                new DuckDBTierPartitionColumn("CustomerId", "CustomerId", "CustomerId", "INTEGER", TierPartitionTransform.Value),
                new DuckDBTierPartitionColumn("InvoiceDate", "InvoiceDate", "InvoiceDate_month", "DATE", TierPartitionTransform.Month),
            ]);

        Assert.Contains("SELECT * EXCLUDE (\"CustomerId\", \"InvoiceDate_month\")", sql);
    }

    [Fact]
    public void Control_table_ddl_upgrades_legacy_tables_with_a_partition_spec()
    {
        var sql = DuckDBTierControl.ControlTableDdl(Sql);

        Assert.Contains("partition_spec TEXT", sql);
        Assert.Contains("ADD COLUMN IF NOT EXISTS partition_spec TEXT", sql);
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
            Sql, "events", null, ["CustomerId"], "Ts", "archive/events", new DateTime(2024, 6, 1),
            [new DuckDBTierPartitionColumn("CustomerId", "INTEGER")]);

        Assert.Contains(
            "FROM (SELECT * REPLACE (CAST(\"CustomerId\" AS INTEGER) AS \"CustomerId\") FROM read_parquet",
            sql);
    }

    [Fact]
    public void DeleteChildSql_matches_rows_whose_root_is_before_the_cutoff()
    {
        var chain = new[] { new DuckDBTierControl.TierJoinHop("InvoiceId", "invoices", null, "Id") };
        var sql = DuckDBTierControl.DeleteChildSql(Sql, "invoice_lines", null, ["Id"], chain, "InvoiceDate", "archive/invoice_lines", new DateTime(2024, 6, 1));

        Assert.Contains("DELETE FROM invoice_lines AS h WHERE h.\"InvoiceId\" IN (SELECT r0.\"Id\" FROM invoices AS r0 WHERE r0.\"InvoiceDate\" < TIMESTAMP '2024-06-01", sql);
        Assert.Contains("EXISTS (SELECT 1 FROM read_parquet('archive/invoice_lines/**/*.parquet'", sql);
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
