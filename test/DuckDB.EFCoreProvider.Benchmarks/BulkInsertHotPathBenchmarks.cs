using BenchmarkDotNet.Attributes;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;

namespace DuckDB.EFCoreProvider.Benchmarks;

/// <summary>
///     Compares the warmed provider bulk-insert path with equivalent direct appender calls.
/// </summary>
[MemoryDiagnoser]
public class BulkInsertHotPathBenchmarks
{
    private const int RowCount = 10_000;

    private static readonly DuckDBAppenderRowWriterAction<IngestRow> ScopedRowWriter =
        static (ref DuckDBAppenderRowWriter writer, IngestRow row) =>
        {
            writer.AppendValue(row.Id);
            writer.AppendValue(row.EventTime);
            writer.AppendValue(row.Amount);
            writer.AppendValue(row.Category);
            writer.AppendValue(row.IsActive);
        };

    private static readonly Action<IDuckDBAppenderRow, IngestRow> ReusableRowWriter =
        static (writer, row) =>
        {
            writer.AppendValue(row.Id);
            writer.AppendValue(row.EventTime);
            writer.AppendValue(row.Amount);
            writer.AppendValue(row.Category);
            writer.AppendValue(row.IsActive);
        };

    private DuckDBConnection _connection = null!;
    private IngestContext _context = null!;
    private IngestRow[] _rows = [];

    [GlobalSetup]
    public void Setup()
    {
        _connection = new DuckDBConnection("DataSource=:memory:");
        _connection.Open();

        using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE benchmark_ingest (
                    id BIGINT NOT NULL,
                    event_time TIMESTAMP NOT NULL,
                    amount DECIMAL(18, 2) NOT NULL,
                    category VARCHAR NOT NULL,
                    is_active BOOLEAN NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<IngestContext>()
            .UseDuckDB(_connection, contextOwnsConnection: false)
            .Options;
        _context = new IngestContext(options);
        _rows = Enumerable.Range(1, RowCount)
            .Select(static index => new IngestRow
            {
                Id = index,
                EventTime = new DateTime(2026, 1, 1).AddSeconds(index),
                Amount = index * 1.25m,
                Category = "category-" + index % 16,
                IsActive = (index & 1) == 0
            })
            .ToArray();

        // Keep the measured provider lane focused on steady-state row ingestion.
        _context.BulkInsert(Array.Empty<IngestRow>());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = RowCount)]
    public int DirectCreateRow()
    {
        using var transaction = _connection.BeginTransaction();
        {
            using var appender = _connection.CreateAppender("main", "benchmark_ingest");
            foreach (var row in _rows)
            {
                appender.CreateRow()
                    .AppendValue(row.Id)
                    .AppendValue(row.EventTime)
                    .AppendValue(row.Amount)
                    .AppendValue(row.Category)
                    .AppendValue(row.IsActive)
                    .EndRow();
            }
        }

        transaction.Rollback();
        return RowCount;
    }

    [Benchmark(OperationsPerInvoke = RowCount)]
    public int DirectScopedAppender()
    {
        using var transaction = _connection.BeginTransaction();
        {
            using var appender = _connection.CreateAppender("main", "benchmark_ingest");
            foreach (var row in _rows)
            {
                appender.AppendRowScoped(row, ScopedRowWriter);
            }
        }

        transaction.Rollback();
        return RowCount;
    }

    [Benchmark(OperationsPerInvoke = RowCount)]
    public int DirectReusableAppender()
    {
        using var transaction = _connection.BeginTransaction();
        {
            using var appender = _connection.CreateAppender("main", "benchmark_ingest");
            foreach (var row in _rows)
            {
                appender.AppendRow(row, ReusableRowWriter);
            }
        }

        transaction.Rollback();
        return RowCount;
    }

    [Benchmark(OperationsPerInvoke = RowCount)]
    public int ProviderBulkInsert()
    {
        using var transaction = _context.Database.BeginTransaction();
        var inserted = _context.BulkInsert(_rows);
        transaction.Rollback();
        return inserted;
    }

    private sealed class IngestContext(DbContextOptions<IngestContext> options) : DbContext(options)
    {
        public DbSet<IngestRow> Rows => Set<IngestRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<IngestRow>(
                entity =>
                {
                    entity.ToTable("benchmark_ingest");
                    entity.HasKey(row => row.Id);
                    entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
                    entity.Property(row => row.EventTime).HasColumnName("event_time");
                    entity.Property(row => row.Amount).HasColumnName("amount").HasPrecision(18, 2);
                    entity.Property(row => row.Category).HasColumnName("category");
                    entity.Property(row => row.IsActive).HasColumnName("is_active");
                });
        }
    }

    private sealed class IngestRow
    {
        public long Id { get; set; }
        public DateTime EventTime { get; set; }
        public decimal Amount { get; set; }
        public string Category { get; set; } = "";
        public bool IsActive { get; set; }
    }
}