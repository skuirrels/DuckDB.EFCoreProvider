using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class TieredStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "duckdb-tier-" + Guid.NewGuid().ToString("N"));

    public TieredStorageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public async Task Archives_whole_aggregate_and_tiered_reads_equal_full_history()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        var (invoices, lines, allocations, allocSum) = TieredTotals(context);

        var result = await context.Database.ArchiveTierAsync<Invoice>(new DateTime(2025, 7, 1).AddMonths(-12));

        Assert.False(result.NoOp);
        Assert.Equal(5, result.RowsArchived);
        // Hot tables shrank; each aggregate table has its own Parquet subdirectory.
        Assert.True(context.Invoices.Count() < invoices);
        Assert.True(Directory.Exists(Path.Combine(_root, "archive", "invoices", "year=2024")));
        Assert.True(Directory.Exists(Path.Combine(_root, "archive", "invoice_lines", "year=2024")));
        Assert.True(Directory.Exists(Path.Combine(_root, "archive", "line_allocations", "year=2024")));
        // Tiered read-models reproduce the full history exactly — no duplicates, no gaps, at every level.
        var (tInvoices, tLines, tAllocations, tAllocSum) = TieredTotals(context);
        Assert.Equal((invoices, lines, allocations, allocSum), (tInvoices, tLines, tAllocations, tAllocSum));
    }

    [Fact]
    public async Task Archive_is_idempotent_on_rerun()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        var before = TieredTotals(context);
        var cutoff = new DateTime(2025, 7, 1).AddMonths(-12);

        await context.Database.ArchiveTierAsync<Invoice>(cutoff);
        var second = await context.Database.ArchiveTierAsync<Invoice>(cutoff);

        Assert.True(second.NoOp);
        Assert.Equal(before, TieredTotals(context));
    }

    [Fact]
    public async Task Reporting_join_across_read_models_spans_hot_and_cold()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        await context.Database.ArchiveTierAsync<Invoice>(new DateTime(2025, 7, 1).AddMonths(-12));

        var revenueByYear =
            (from l in context.LineHistory
             join i in context.InvoiceHistory on l.InvoiceId equals i.Id
             group l.Amount by i.InvoiceDate.Year into g
             select new { Year = g.Key, Revenue = g.Sum() }).ToList();

        // Both the cold (2024) and hot (2025) years must appear in one query.
        Assert.Contains(revenueByYear, r => r.Year == 2024);
        Assert.Contains(revenueByYear, r => r.Year == 2025);
        Assert.Equal(context.LineHistory.Sum(l => l.Amount), revenueByYear.Sum(r => r.Revenue));
    }

    [Fact]
    public void Hot_writes_and_include_are_unaffected_by_tiering()
    {
        using var context = CreateContext();

        // Normal EF: write a root with a child graph, then Include across the aggregate.
        var invoice = new Invoice { InvoiceDate = new DateTime(2025, 6, 1) };
        var line = new InvoiceLine { Amount = 42 };
        line.Allocations.Add(new LineAllocation { Amount = 42 });
        invoice.Lines.Add(line);
        context.Invoices.Add(invoice);
        context.SaveChanges();

        using var reader = CreateContext();
        var loaded = reader.Invoices.Include(i => i.Lines).ThenInclude(l => l.Allocations).Single();
        Assert.Single(loaded.Lines);
        Assert.Single(loaded.Lines[0].Allocations);
        Assert.Equal(42, loaded.Lines[0].Allocations[0].Amount);
    }

    [Fact]
    public void Ensure_created_alone_creates_all_aggregate_views()
    {
        using var context = CreateContext(); // EnsureCreated only; no explicit EnsureTieredStoresCreated
        context.Invoices.Add(new Invoice { InvoiceDate = new DateTime(2025, 6, 1), Lines = { new InvoiceLine { Amount = 1 } } });
        context.SaveChanges();

        // Querying every tiered view must not raise a "view does not exist" error.
        Assert.Equal(1, context.InvoiceHistory.Count());
        Assert.Equal(1, context.LineHistory.Count());
    }

    [Fact]
    public async Task Purge_drops_a_period_across_every_aggregate_table()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        await context.Database.ArchiveTierAsync<Invoice>(new DateTime(2025, 7, 1).AddMonths(-12));
        var beforeInvoices = context.InvoiceHistory.Count();

        var purged = context.Database.PurgeArchiveOlderThan<Invoice>(new DateTime(2024, 4, 1));

        Assert.Equal(6, purged); // 2 months × 3 tables
        Assert.Equal(beforeInvoices - 2, context.InvoiceHistory.Count());
    }

    [Fact]
    public async Task Purge_that_empties_the_archive_falls_back_to_a_hot_only_view()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        await context.Database.ArchiveTierAsync<Invoice>(new DateTime(2025, 7, 1).AddMonths(-12));

        // Purge past all cold data: every archived partition is removed for every aggregate table.
        var purged = context.Database.PurgeArchiveOlderThan<Invoice>(new DateTime(2025, 7, 1));

        Assert.True(purged > 0);
        // The views must fall back to hot-only rather than error on an empty read_parquet glob.
        Assert.Equal(context.Invoices.Count(), context.InvoiceHistory.Count());
        Assert.Equal(context.Set<InvoiceLine>().Count(), context.LineHistory.Count());
    }

    [Fact]
    public void Reserved_partition_column_name_is_rejected_at_model_validation()
    {
        using var context = new ReservedColumnContext(Path.Combine(_root, "s.duckdb"), Path.Combine(_root, "a"));
        var ex = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("collides with the hive partition column", ex.Message);
    }

    [Fact]
    public void Overlapping_aggregate_archive_paths_are_rejected_at_model_validation()
    {
        using var context = new OverlapContext(Path.Combine(_root, "s.duckdb"), Path.Combine(_root, "shared"));
        var ex = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("overlapping", ex.Message);
    }

    [Fact]
    public void Missing_child_navigation_is_rejected_at_model_validation()
    {
        using var context = new BadNavigationContext(Path.Combine(_root, "s.duckdb"), Path.Combine(_root, "a"));
        var ex = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("was not found", ex.Message);
    }

    [Fact]
    public void Read_model_column_not_on_source_is_rejected_at_model_validation()
    {
        using var context = new BadReadModelContext(Path.Combine(_root, "s.duckdb"), Path.Combine(_root, "a"));
        var ex = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("must mirror", ex.Message);
    }

    [Fact]
    public async Task Crash_between_copy_and_delete_self_heals_without_double_counting()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        var expected = TieredTotals(context);
        var cutoff = new DateTime(2025, 7, 1).AddMonths(-12);

        var result = await context.Database.ArchiveTierAsync<Invoice>(cutoff);

        // Simulate a crash after COPY but before DELETE: the archived rows are back in the hot tables.
        ReinsertArchivedIntoHot(context);
        Assert.Equal(expected, TieredTotals(context)); // views still exact — no double counting

        var heal = await context.Database.ArchiveTierAsync<Invoice>(cutoff);
        Assert.True(heal.NoOp);
        Assert.Equal(expected, TieredTotals(context));
        Assert.Equal(result.Watermark, heal.Watermark);
    }

    [Fact]
    public async Task Late_hot_rows_before_existing_watermark_remain_visible_and_are_not_deleted()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        var cutoff = new DateTime(2025, 7, 1).AddMonths(-12);

        await context.Database.ArchiveTierAsync<Invoice>(cutoff);
        var before = TieredTotals(context);

        context.Invoices.Add(new Invoice { InvoiceDate = cutoff.AddMonths(-1) });
        context.SaveChanges();

        Assert.Equal(before.Invoices + 1, context.InvoiceHistory.Count());

        var rerun = await context.Database.ArchiveTierAsync<Invoice>(cutoff);

        Assert.True(rerun.NoOp);
        Assert.Equal(before.Invoices + 1, context.InvoiceHistory.Count());
    }

    [Fact]
    public async Task Noop_archive_rerun_does_not_delete_late_hot_rows_before_existing_watermark()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        var cutoff = new DateTime(2025, 7, 1).AddMonths(-12);

        await context.Database.ArchiveTierAsync<Invoice>(cutoff);
        var hotRowsAfterArchive = context.Invoices.Count();

        context.Invoices.Add(new Invoice { InvoiceDate = cutoff.AddMonths(-1) });
        context.SaveChanges();

        Assert.Equal(hotRowsAfterArchive + 1, context.Invoices.Count());

        var rerun = await context.Database.ArchiveTierAsync<Invoice>(cutoff);

        Assert.True(rerun.NoOp);
        Assert.Equal(hotRowsAfterArchive + 1, context.Invoices.Count());
    }

    [Fact]
    public void Cold_files_without_a_watermark_do_not_hide_hot_rows_when_views_are_regenerated()
    {
        using var context = CreateContext();
        Seed(context, months: 3, baseDate: new DateTime(2025, 7, 1));
        var archive = Path.Combine(_root, "archive", "invoices");
        Directory.CreateDirectory(archive);

#pragma warning disable EF1002, EF1003 // archive is a test-owned temp path, not user input
        context.Database.ExecuteSqlRaw(
            $"""
             COPY (
                 SELECT "Id", "InvoiceDate", year("InvoiceDate") AS "year", month("InvoiceDate") AS "month"
                   FROM invoices
             )
             TO '{archive.Replace("'", "''")}'
             (FORMAT PARQUET, PARTITION_BY ("year", "month"), OVERWRITE_OR_IGNORE);
             """);
#pragma warning restore EF1002, EF1003

        context.Database.EnsureTieredStoresCreated();

        Assert.Equal(context.Invoices.Count(), context.InvoiceHistory.Count());
    }

    [Fact]
    public void Tiered_storage_honors_schema_mapped_hot_tables()
    {
        using var context = new SchemaContext(Path.Combine(_root, "schema.duckdb"), Path.Combine(_root, "schema-archive"));

        context.Database.EnsureCreated();
        context.Invoices.Add(new Invoice { InvoiceDate = new DateTime(2025, 6, 1) });
        context.SaveChanges();

        Assert.Equal(1, context.InvoiceHistory.Count());
    }

    [Fact]
    public void Purge_skips_malformed_partition_directories()
    {
        using var context = CreateContext();
        Directory.CreateDirectory(Path.Combine(_root, "archive", "invoices", "year=2024", "month=99"));

        var purged = context.Database.PurgeArchiveOlderThan<Invoice>(new DateTime(2025, 1, 1));

        Assert.Equal(0, purged);
    }

    [Fact]
    public async Task Column_added_after_archiving_reads_null_for_cold_rows()
    {
        var dbPath = Path.Combine(_root, "store.duckdb");
        var archivePath = Path.Combine(_root, "archive");

        using (var context = CreateContext())
        {
            Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
            await context.Database.ArchiveTierAsync<Invoice>(new DateTime(2025, 7, 1).AddMonths(-12));
        }

        using (var evolved = new EvolvedContext(dbPath, archivePath))
        {
            evolved.Database.ExecuteSqlRaw("ALTER TABLE invoices ADD COLUMN \"Note\" TEXT;");
            evolved.Database.EnsureTieredStoresCreated(); // regenerate the view over the new schema

            var all = evolved.InvoiceHistory.OrderBy(i => i.Id).ToList();
            Assert.Equal(18, all.Count);
            Assert.All(all.Where(i => i.Id <= 5), i => Assert.Null(i.Note)); // the 5 archived (cold) invoices
        }
    }

    private void ReinsertArchivedIntoHot(InvoiceContext context)
    {
        var archive = Path.Combine(_root, "archive");

        void Copy(string table, string columns)
        {
            var glob = Path.Combine(archive, table).Replace("'", "''") + "/**/*.parquet";
#pragma warning disable EF1002, EF1003 // table/columns/glob are test constants, not user input
            context.Database.ExecuteSqlRaw(
                $"INSERT INTO {table} SELECT {columns} FROM read_parquet('{glob}', hive_partitioning=true, union_by_name=true);");
#pragma warning restore EF1002, EF1003
        }

        // Foreign-key order: parents before children.
        Copy("invoices", "\"Id\", \"InvoiceDate\"");
        Copy("invoice_lines", "\"Id\", \"InvoiceId\", \"Amount\"");
        Copy("line_allocations", "\"Id\", \"InvoiceLineId\", \"Amount\"");
    }

    private static (int Invoices, int Lines, int Allocations, decimal AllocSum) TieredTotals(InvoiceContext c)
        => (c.InvoiceHistory.Count(), c.LineHistory.Count(), c.AllocationHistory.Count(), c.AllocationHistory.Sum(a => a.Amount));

    private static void Seed(InvoiceContext context, int months, DateTime baseDate)
    {
        for (var m = months - 1; m >= 0; m--)
        {
            var invoice = new Invoice { InvoiceDate = baseDate.AddMonths(-m) };
            for (var line = 0; line < 2; line++)
            {
                var amount = (m + 1) * 10 + line;
                invoice.Lines.Add(new InvoiceLine { Amount = amount, Allocations = { new LineAllocation { Amount = amount } } });
            }

            context.Invoices.Add(invoice);
        }

        context.SaveChanges();
    }

    private InvoiceContext CreateContext()
    {
        var context = new InvoiceContext(Path.Combine(_root, "store.duckdb"), Path.Combine(_root, "archive"));
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class Invoice
    {
        public int Id { get; set; }
        public DateTime InvoiceDate { get; set; }
        public List<InvoiceLine> Lines { get; set; } = [];
    }

    private sealed class InvoiceLine
    {
        public int Id { get; set; }
        public int InvoiceId { get; set; }
        public Invoice? Invoice { get; set; }
        public decimal Amount { get; set; }
        public List<LineAllocation> Allocations { get; set; } = [];
    }

    private sealed class LineAllocation
    {
        public int Id { get; set; }
        public int InvoiceLineId { get; set; }
        public InvoiceLine? InvoiceLine { get; set; }
        public decimal Amount { get; set; }
    }

    private sealed class InvoiceRm { public int Id { get; set; } public DateTime InvoiceDate { get; set; } }
    private sealed class InvoiceLineRm { public int Id { get; set; } public int InvoiceId { get; set; } public decimal Amount { get; set; } }
    private sealed class LineAllocationRm { public int Id { get; set; } public int InvoiceLineId { get; set; } public decimal Amount { get; set; } }

    private interface IArchivePathContext
    {
        string ArchivePath { get; }
    }

    private sealed class InvoiceContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<InvoiceRm> InvoiceHistory => Set<InvoiceRm>();
        public DbSet<InvoiceLineRm> LineHistory => Set<InvoiceLineRm>();
        public DbSet<LineAllocationRm> AllocationHistory => Set<LineAllocationRm>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(b =>
            {
                b.ToTable("invoices");
                b.HasKey(i => i.Id);
                b.HasMany(i => i.Lines).WithOne(l => l.Invoice).HasForeignKey(l => l.InvoiceId);
            });
            modelBuilder.Entity<InvoiceLine>(b =>
            {
                b.ToTable("invoice_lines");
                b.HasKey(l => l.Id);
                b.HasMany(l => l.Allocations).WithOne(a => a.InvoiceLine).HasForeignKey(a => a.InvoiceLineId);
            });
            modelBuilder.Entity<LineAllocation>(b => { b.ToTable("line_allocations"); b.HasKey(a => a.Id); });

            modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, archivePath, TierGranularity.Month)
                .WithReadModel<InvoiceRm>()
                .Including<InvoiceLine>(i => i.Lines, line => line
                    .WithReadModel<InvoiceLineRm>()
                    .Including<LineAllocation>(l => l.Allocations, alloc => alloc.WithReadModel<LineAllocationRm>()));
        }
    }

    private sealed class BadNavigationContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Invoice has no relationship to InvoiceLine, so the .Including navigation has no foreign key.
            modelBuilder.Entity<Invoice>(b => { b.ToTable("invoices"); b.HasKey(i => i.Id); b.Ignore(i => i.Lines); });
            modelBuilder.Entity<InvoiceLine>(b => { b.ToTable("invoice_lines"); b.HasKey(l => l.Id); b.Ignore(l => l.Allocations); });
            modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, archivePath)
                .Including<InvoiceLine>(i => i.Lines);
        }
    }

    private sealed class BadReadModelContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(b => { b.ToTable("invoices"); b.HasKey(i => i.Id); b.Ignore(i => i.Lines); });
            // MismatchRm has a column the invoices table does not.
            modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, archivePath).WithReadModel<MismatchRm>();
        }

        private sealed class MismatchRm { public int Id { get; set; } public DateTime InvoiceDate { get; set; } public string? Nonexistent { get; set; } }
    }

    private sealed class Ledger { public int Id { get; set; } public DateTime PostedAt { get; set; } }

    // Maps a property to the reserved hive partition column name "year".
    private sealed class ReservedColumnContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(b =>
            {
                b.ToTable("invoices");
                b.HasKey(i => i.Id);
                b.Ignore(i => i.Lines);
                b.Property(i => i.Id).HasColumnName("year");
            });
            modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, archivePath);
        }
    }

    // Two aggregate roots sharing the same archive directory.
    private sealed class OverlapContext(string dbPath, string archiveRoot) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archiveRoot;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(b => { b.ToTable("invoices"); b.HasKey(i => i.Id); b.Ignore(i => i.Lines); });
            modelBuilder.Entity<Ledger>(b => { b.ToTable("ledger"); b.HasKey(l => l.Id); });
            modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, archiveRoot);
            modelBuilder.ToTieredStore<Ledger>(l => l.PostedAt, archiveRoot);
        }
    }

    // Second-generation root model over the same "invoices" table/archive, with an added column.
    private sealed class InvoiceV2 { public int Id { get; set; } public DateTime InvoiceDate { get; set; } public string? Note { get; set; } }
    private sealed class InvoiceV2Rm { public int Id { get; set; } public DateTime InvoiceDate { get; set; } public string? Note { get; set; } }

    private sealed class EvolvedContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<InvoiceV2Rm> InvoiceHistory => Set<InvoiceV2Rm>();
        public string ArchivePath => archivePath + "|evolved";

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<InvoiceV2>().ToTable("invoices");
            modelBuilder.ToTieredStore<InvoiceV2>(i => i.InvoiceDate, archivePath, TierGranularity.Month)
                .WithReadModel<InvoiceV2Rm>();
        }
    }

    private sealed class SchemaContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<InvoiceRm> InvoiceHistory => Set<InvoiceRm>();
        public string ArchivePath => archivePath + "|schema";

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(b =>
            {
                b.ToTable("invoices", "accounting");
                b.HasKey(i => i.Id);
                b.Ignore(i => i.Lines);
            });

            modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, archivePath, TierGranularity.Month)
                .WithReadModel<InvoiceRm>();
        }
    }

    private sealed class ArchivePathModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => (context.GetType(), (context as IArchivePathContext)?.ArchivePath, designTime);
    }
}
