using BenchmarkDotNet.Attributes;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;

namespace DuckDB.EFCoreProvider.Benchmarks;

/// <summary>
///     Measures alternate-key Upsert as the indexed target grows, comparing the released 500-row chunk
///     with the width-aware default bounded by the provider's staged-cell budget.
/// </summary>
[MemoryDiagnoser]
public class UpsertTargetScaleBenchmarks
{
    private const int IncomingRowCount = 10_000;
    private const int ExistingRowCount = 8_000;

    private string _dbPath = "";
    private List<TargetScaleRow> _rows = [];

    [Params(10_000, 1_000_000)]
    public int TargetRowCount { get; set; }

    [IterationSetup]
    public void IterationSetup()
    {
        _dbPath = BenchmarkFiles.NewDbPath("upsert_target_scale");
        using var context = new TargetScaleContext(_dbPath);
        context.Database.EnsureCreated();
        context.Database.OpenConnection();

        var connection = (DuckDBConnection)context.Database.GetDbConnection();
        using (var seed = connection.CreateCommand())
        {
            seed.CommandText = $"""
                INSERT INTO items (external_id, quantity, payload)
                SELECT CAST(md5(CAST(i AS VARCHAR)) AS UUID), CAST(i AS INTEGER), 'seed-' || i
                FROM range({TargetRowCount}) AS source(i);
                """;
            seed.ExecuteNonQuery();
        }

        var keys = new List<Guid>(ExistingRowCount);
        using (var readKeys = connection.CreateCommand())
        {
            readKeys.CommandText = $"SELECT external_id FROM items USING SAMPLE {ExistingRowCount} ROWS (reservoir, 1729);";
            using var reader = readKeys.ExecuteReader();
            while (reader.Read())
            {
                keys.Add(reader.GetGuid(0));
            }
        }

        _rows = new List<TargetScaleRow>(IncomingRowCount);
        _rows.AddRange(keys.Select((key, index) => new TargetScaleRow
        {
            ExternalId = key,
            Quantity = 1_000_000 + index,
            Payload = "update-" + index,
        }));
        _rows.AddRange(Enumerable.Range(keys.Count, IncomingRowCount - keys.Count).Select(index => new TargetScaleRow
        {
            ExternalId = TargetScaleData.NewExternalId(index),
            Quantity = 1_000_000 + index,
            Payload = "insert-" + index,
        }));
    }

    [IterationCleanup]
    public void IterationCleanup()
        => BenchmarkFiles.DeleteDb(_dbPath);

    [Benchmark(Baseline = true)]
    public async Task<int> Released500RowChunkAsync()
    {
        await using var context = new TargetScaleContext(_dbPath);
        return await context.UpsertAsync(_rows, row => row.ExternalId, batchSize: 500);
    }

    [Benchmark]
    public async Task<int> WidthAwareDefaultAsync()
    {
        await using var context = new TargetScaleContext(_dbPath);
        return await context.UpsertAsync(_rows, row => row.ExternalId);
    }
}

/// <summary>
///     Measures one million alternate-key Upsert inputs against a one-million-row indexed target.
///     Database creation, cloning, and result verification are outside the timed operation.
/// </summary>
[MemoryDiagnoser]
public class UpsertMillionRowBenchmarks
{
    private const int TargetRowCount = 1_000_000;
    private const int IncomingRowCount = 1_000_000;
    private const int ExistingRowCount = 800_000;

    private string _baselineDbPath = "";
    private string _iterationDbPath = "";
    private List<TargetScaleRow> _rows = [];

    [GlobalSetup]
    public void GlobalSetup()
    {
        _baselineDbPath = BenchmarkFiles.NewDbPath("upsert_million_baseline");
        using var context = new TargetScaleContext(_baselineDbPath);
        context.Database.EnsureCreated();
        context.Database.OpenConnection();

        var connection = (DuckDBConnection)context.Database.GetDbConnection();
        using (var seed = connection.CreateCommand())
        {
            seed.CommandText = $"""
                INSERT INTO items (external_id, quantity, payload)
                SELECT CAST(md5(CAST(i AS VARCHAR)) AS UUID), CAST(i AS INTEGER), 'seed-' || i
                FROM range({TargetRowCount}) AS source(i);
                """;
            seed.ExecuteNonQuery();
        }

        var keys = new List<Guid>(ExistingRowCount);
        using (var readKeys = connection.CreateCommand())
        {
            readKeys.CommandText = $"SELECT external_id FROM items LIMIT {ExistingRowCount};";
            using var reader = readKeys.ExecuteReader();
            while (reader.Read())
            {
                keys.Add(reader.GetGuid(0));
            }
        }

        _rows = new List<TargetScaleRow>(IncomingRowCount);
        _rows.AddRange(keys.Select((key, index) => new TargetScaleRow
        {
            ExternalId = key,
            Quantity = 2_000_000 + index,
            Payload = "update-" + index,
        }));
        _rows.AddRange(Enumerable.Range(keys.Count, IncomingRowCount - keys.Count).Select(index => new TargetScaleRow
        {
            ExternalId = TargetScaleData.NewExternalId(index),
            Quantity = 2_000_000 + index,
            Payload = "insert-" + index,
        }));

        using var checkpoint = connection.CreateCommand();
        checkpoint.CommandText = "CHECKPOINT;";
        checkpoint.ExecuteNonQuery();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _iterationDbPath = BenchmarkFiles.NewDbPath("upsert_million_iteration");
        File.Copy(_baselineDbPath, _iterationDbPath);
    }

    [Benchmark]
    public async Task<int> UpsertOneMillionAsync()
    {
        await using var context = new TargetScaleContext(_iterationDbPath);
        return await context.UpsertAsync(_rows, row => row.ExternalId);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        try
        {
            using var context = new TargetScaleContext(_iterationDbPath);
            var expectedTargetCount = TargetRowCount + IncomingRowCount - ExistingRowCount;
            var targetCount = context.Rows.Count();
            var appliedCount = context.Rows.Count(row => row.Quantity >= 2_000_000);

            if (targetCount != expectedTargetCount || appliedCount != IncomingRowCount)
            {
                throw new InvalidOperationException(
                    $"One-million-row Upsert verification failed: target={targetCount}/{expectedTargetCount}, "
                    + $"applied={appliedCount}/{IncomingRowCount}.");
            }
        }
        finally
        {
            BenchmarkFiles.DeleteDb(_iterationDbPath);
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
        => BenchmarkFiles.DeleteDb(_baselineDbPath);
}

internal sealed class TargetScaleContext(string dbPath) : DbContext
{
    internal DbSet<TargetScaleRow> Rows => Set<TargetScaleRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseDuckDB("DataSource=" + dbPath);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TargetScaleRow>(entity =>
        {
            entity.ToTable("items");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").UseAutoIncrement();
            entity.Property(row => row.ExternalId).HasColumnName("external_id");
            entity.Property(row => row.Quantity).HasColumnName("quantity");
            entity.Property(row => row.Payload).HasColumnName("payload");
            entity.HasAlternateKey(row => row.ExternalId);
        });
    }
}

internal sealed class TargetScaleRow
{
    public long Id { get; set; }
    public Guid ExternalId { get; set; }
    public int Quantity { get; set; }
    public string Payload { get; set; } = "";
}

internal static class TargetScaleData
{
    internal static Guid NewExternalId(int index)
        => new(index, -16_385, 16_383, 0x80, 0x00, 0x55, 0x50, 0x53, 0x45, 0x52, 0x54);
}
