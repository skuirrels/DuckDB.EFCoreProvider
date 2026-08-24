using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Buffers;
using System.Collections.ObjectModel;

namespace DuckDB.EFCoreProvider.Benchmarks;

/// <summary>
///     Measures only the native Upsert merge statement when the staged conflict keys are known to be unique.
/// </summary>
[MemoryDiagnoser]
public class DistinctInputUpsertBenchmarks
{
    private DuckDBConnection _connection = null!;
    private DuckDBTransaction _transaction = null!;
    private string _directSql = "";
    private string _windowSql = "";

    [Params(10_000, 50_000)]
    public int RowCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _connection = new DuckDBConnection("DataSource=:memory:");
        _connection.Open();
        Execute(
            """
            CREATE TABLE window_target (
                id INTEGER PRIMARY KEY,
                name VARCHAR NOT NULL,
                quantity INTEGER NOT NULL,
                amount DOUBLE NOT NULL
            );
            CREATE TEMPORARY TABLE window_stage AS SELECT * FROM window_target WHERE false;
            """);

        const string columns = "id, name, quantity, amount";
        const string suffix =
            " ON CONFLICT (id) DO UPDATE SET name = excluded.name, "
            + "quantity = excluded.quantity, amount = excluded.amount;";
        _windowSql =
            $"INSERT INTO window_target ({columns}) "
            + $"SELECT {columns} FROM (SELECT {columns}, "
            + "row_number() OVER (PARTITION BY id ORDER BY rowid DESC) AS __row_number "
            + "FROM window_stage) AS ranked WHERE __row_number = 1"
            + suffix;
        _directSql =
            $"INSERT INTO window_target ({columns}) SELECT {columns} FROM window_stage"
            + suffix;
    }

    [GlobalCleanup]
    public void GlobalCleanup()
        => _connection.Dispose();

    [IterationSetup]
    public void IterationSetup()
    {
        _transaction = _connection.BeginTransaction();
        Execute("DELETE FROM window_target; DELETE FROM window_stage;");
        Execute(
            $"INSERT INTO window_target SELECT id, 'old-' || id, id, id * 1.25 "
            + $"FROM range(0, {RowCount / 2}) AS rows(id);");
        Execute(
            $"INSERT INTO window_stage SELECT id, 'new-' || id, id * 2, id * 2.5 "
            + $"FROM range(0, {RowCount}) AS rows(id);");
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _transaction.Rollback();
        _transaction.Dispose();
    }

    [Benchmark(Baseline = true)]
    public int WindowDeduplication()
        => Execute(_windowSql);

    [Benchmark]
    public int CallerGuaranteedDistinct()
        => Execute(_directSql);

    private int Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }
}

/// <summary>
///     Measures the transaction-boundary proposal independently of staging and Appender work.
/// </summary>
[MemoryDiagnoser]
public class UpsertTransactionBoundaryBenchmarks
{
    private const int RowCount = 100_000;
    private const int BatchSize = 10_000;

    private DuckDBConnection _connection = null!;
    private string _dbPath = "";
    private string[] _batchCommands = [];

    [GlobalSetup]
    public void GlobalSetup()
    {
        _dbPath = BenchmarkFiles.NewDbPath("upsert_transaction_boundary");
        _connection = new DuckDBConnection("DataSource=" + _dbPath);
        _connection.Open();
        _batchCommands = Enumerable.Range(0, RowCount / BatchSize)
            .Select(
                batch =>
                {
                    var start = batch * BatchSize;
                    var end = start + BatchSize;
                    return $"INSERT INTO transaction_target SELECT id, 'new-' || id, repeat('x', 128), id * 2.5 "
                           + $"FROM range({start}, {end}) AS rows(id) ON CONFLICT (id) DO UPDATE SET "
                           + "name = excluded.name, payload = excluded.payload, amount = excluded.amount;";
                })
            .ToArray();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _connection.Dispose();
        BenchmarkFiles.DeleteDb(_dbPath);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        Execute("DROP TABLE IF EXISTS transaction_target;");
        Execute(
            """
            CREATE TABLE transaction_target (
                id INTEGER PRIMARY KEY,
                name VARCHAR NOT NULL,
                payload VARCHAR NOT NULL,
                amount DOUBLE NOT NULL
            );
            """);
        Execute(
            $"INSERT INTO transaction_target SELECT id, 'old-' || id, repeat('o', 128), id * 1.25 "
            + $"FROM range(0, {RowCount / 2}) AS rows(id);");
        Execute("CHECKPOINT;");
    }

    [Benchmark(Baseline = true)]
    public int AutoCommitPerBatch()
        => ExecuteBatches();

    [Benchmark]
    public int OneExplicitTransaction()
    {
        using var transaction = _connection.BeginTransaction();
        var affected = ExecuteBatches();
        transaction.Commit();
        return affected;
    }

    private int ExecuteBatches()
    {
        var affected = 0;
        foreach (var sql in _batchCommands)
        {
            affected += Execute(sql);
        }

        return affected;
    }

    private int Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }
}

/// <summary>
///     Measures the provider's complete staged Upsert path with and without a caller-owned transaction.
/// </summary>
[MemoryDiagnoser]
public class ProviderUpsertTransactionBenchmarks
{
    private const int RowCount = 100_000;
    private const int BatchSize = 10_000;

    private string _dbPath = "";
    private TransactionContext _context = null!;
    private TransactionRow[] _rows = [];

    [GlobalSetup]
    public void GlobalSetup()
        => _rows = Enumerable.Range(0, RowCount)
            .Select(
                static id => new TransactionRow
                {
                    Id = id,
                    Name = "new-" + id,
                    Payload = new string('x', 128),
                    Amount = id * 2.5
                })
            .ToArray();

    [IterationSetup]
    public void IterationSetup()
    {
        _dbPath = BenchmarkFiles.NewDbPath("provider_upsert_transaction");
        _context = new TransactionContext(_dbPath);
        _context.Database.EnsureCreated();
        _context.BulkInsert(_rows.Take(RowCount / 2));
        _context.Database.ExecuteSqlRaw("CHECKPOINT;");
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _context.Dispose();
        BenchmarkFiles.DeleteDb(_dbPath);
    }

    [Benchmark(Baseline = true)]
    public int ProviderAutoCommit()
        => _context.Upsert(_rows, BatchSize);

    [Benchmark]
    public int ProviderExplicitTransaction()
    {
        using var transaction = _context.Database.BeginTransaction();
        var affected = _context.Upsert(_rows, BatchSize);
        transaction.Commit();
        return affected;
    }

    private sealed class TransactionContext(string dbPath) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB("DataSource=" + dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<TransactionRow>().Property(row => row.Id).ValueGeneratedNever();
    }

    private sealed class TransactionRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Payload { get; set; } = "";
        public double Amount { get; set; }
    }
}

/// <summary>
///     Compares the current correlated logical-key cardinality guard with an atomic set-based guard.
/// </summary>
[MemoryDiagnoser]
public class LogicalKeyCardinalityBenchmarks
{
    private const int IncomingRowCount = 10_000;
    private const string AtomicSetGuardSql =
        """
        MERGE INTO logical_target AS target
        USING (
            SELECT incoming.*
            FROM logical_stage AS incoming
            CROSS JOIN (
                SELECT CASE WHEN count(*) > 0
                    THEN error('duplicate logical key') ELSE true END AS valid
                FROM (
                    SELECT existing.logical_key
                    FROM logical_target AS existing
                    SEMI JOIN logical_stage AS staged
                        ON existing.logical_key = staged.logical_key
                    GROUP BY existing.logical_key
                    HAVING count(*) > 1
                    LIMIT 1
                ) AS duplicate_keys
            ) AS cardinality_guard
            WHERE cardinality_guard.valid
        ) AS source
        ON target.logical_key = source.logical_key
        WHEN MATCHED THEN UPDATE SET payload = source.payload
        WHEN NOT MATCHED THEN INSERT (id, logical_key, payload)
            VALUES (source.id, source.logical_key, source.payload);
        """;

    private DuckDBConnection _connection = null!;
    private DuckDBTransaction _transaction = null!;

    [Params(100_000, 500_000)]
    public int TargetRowCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _connection = new DuckDBConnection("DataSource=:memory:");
        _connection.Open();
        Execute(
            """
            CREATE TABLE logical_target (
                id INTEGER NOT NULL,
                logical_key VARCHAR NOT NULL,
                payload INTEGER NOT NULL
            );
            CREATE TEMPORARY TABLE logical_stage AS SELECT * FROM logical_target WHERE false;
            """);
        VerifyAtomicGuardRejectsDuplicatesBeforeMutation();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
        => _connection.Dispose();

    [IterationSetup]
    public void IterationSetup()
    {
        _transaction = _connection.BeginTransaction();
        Execute("DELETE FROM logical_target; DELETE FROM logical_stage;");
        Execute(
            $"INSERT INTO logical_target SELECT id, 'key-' || id, id "
            + $"FROM range(0, {TargetRowCount}) AS rows(id);");
        Execute(
            $"INSERT INTO logical_stage SELECT id, 'key-' || id, id * 2 "
            + $"FROM range({TargetRowCount - (IncomingRowCount / 2)}, "
            + $"{TargetRowCount + (IncomingRowCount / 2)}) AS rows(id);");
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _transaction.Rollback();
        _transaction.Dispose();
    }

    [Benchmark(Baseline = true)]
    public int CorrelatedGuardInsideMerge()
        => Execute(
            """
            MERGE INTO logical_target AS target
            USING (
                SELECT incoming.*
                FROM logical_stage AS incoming
                WHERE CASE WHEN (
                    SELECT count(*)
                    FROM logical_target AS existing
                    WHERE existing.logical_key = incoming.logical_key
                ) > 1 THEN error('duplicate logical key') ELSE true END
            ) AS source
            ON target.logical_key = source.logical_key
            WHEN MATCHED THEN UPDATE SET payload = source.payload
            WHEN NOT MATCHED THEN INSERT (id, logical_key, payload)
                VALUES (source.id, source.logical_key, source.payload);
            """);

    [Benchmark]
    public int AtomicSetGuardInsideMerge()
        => Execute(AtomicSetGuardSql);

    private void VerifyAtomicGuardRejectsDuplicatesBeforeMutation()
    {
        Execute(
            """
            INSERT INTO logical_target VALUES (1, 'duplicate', 10), (2, 'duplicate', 20);
            INSERT INTO logical_stage VALUES (3, 'duplicate', 99);
            """);
        try
        {
            Execute(AtomicSetGuardSql);
            throw new InvalidOperationException("The atomic set guard accepted a duplicate target key.");
        }
        catch (DuckDBException exception) when (exception.Message.Contains("duplicate logical key", StringComparison.Ordinal))
        {
        }

        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT count(*), sum(payload) FROM logical_target WHERE logical_key = 'duplicate';";
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt64(0) != 2 || reader.GetInt64(1) != 30)
        {
            throw new InvalidOperationException("The atomic set guard mutated the duplicate target before failing.");
        }

        Execute("DELETE FROM logical_target; DELETE FROM logical_stage;");
    }

    private int Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }
}

/// <summary>
///     Compares Appender-backed object ingestion with a pre-existing Parquet source.
/// </summary>
[MemoryDiagnoser]
public class ParquetIngestionModalityBenchmarks
{
    private const int RowCount = 100_000;

    private DuckDBConnection _connection = null!;
    private ParquetContext _context = null!;
    private IDbContextTransaction _transaction = null!;
    private string _parquetPath = "";
    private ParquetRow[] _rows = [];

    [GlobalSetup]
    public void GlobalSetup()
    {
        _parquetPath = Path.Combine(Path.GetTempPath(), "provider_ingest_" + Guid.NewGuid().ToString("N") + ".parquet");
        _connection = new DuckDBConnection("DataSource=:memory:");
        _connection.Open();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE parquet_target (
                    id BIGINT NOT NULL,
                    name VARCHAR NOT NULL,
                    amount DOUBLE NOT NULL,
                    active BOOLEAN NOT NULL
                );
                """;
            command.ExecuteNonQuery();
            command.CommandText =
                $"COPY (SELECT id::BIGINT AS id, 'row-' || id AS name, id * 1.25 AS amount, "
                + $"(id % 2) = 0 AS active FROM range(1, {RowCount + 1}) AS rows(id)) "
                + $"TO '{EscapeLiteral(_parquetPath)}' (FORMAT PARQUET);";
            command.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<ParquetContext>()
            .UseDuckDB(_connection, contextOwnsConnection: false)
            .Options;
        _context = new ParquetContext(options);
        _rows = Enumerable.Range(1, RowCount)
            .Select(
                static id => new ParquetRow
                {
                    Id = id,
                    Name = "row-" + id,
                    Amount = id * 1.25,
                    Active = (id & 1) == 0
                })
            .ToArray();
        _context.BulkInsert(Array.Empty<ParquetRow>());
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _context.Dispose();
        _connection.Dispose();
        File.Delete(_parquetPath);
    }

    [IterationSetup]
    public void IterationSetup()
        => _transaction = _context.Database.BeginTransaction();

    [IterationCleanup]
    public void IterationCleanup()
    {
        _transaction.Rollback();
        _transaction.Dispose();
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = RowCount)]
    public int ProviderBulkInsertFromObjects()
        => _context.BulkInsert(_rows);

    [Benchmark(OperationsPerInvoke = RowCount)]
    public int InsertFromParquet()
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            $"INSERT INTO parquet_target (id, name, amount, active) "
            + $"SELECT id, name, amount, active FROM read_parquet('{EscapeLiteral(_parquetPath)}');";
        return command.ExecuteNonQuery();
    }

    private static string EscapeLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed class ParquetContext(DbContextOptions<ParquetContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ParquetRow>(
                entity =>
                {
                    entity.ToTable("parquet_target");
                    entity.HasKey(row => row.Id);
                    entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
                    entity.Property(row => row.Name).HasColumnName("name");
                    entity.Property(row => row.Amount).HasColumnName("amount");
                    entity.Property(row => row.Active).HasColumnName("active");
                });
        }
    }

    private sealed class ParquetRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public double Amount { get; set; }
        public bool Active { get; set; }
    }
}

/// <summary>
///     Measures the real provider Appender path for three value-type converters versus provider-shaped properties.
/// </summary>
[MemoryDiagnoser]
public class ConvertedBulkInsertBenchmarks
{
    private const int RowCount = 100_000;

    private DuckDBConnection _connection = null!;
    private ConvertedContext _convertedContext = null!;
    private DirectContext _directContext = null!;
    private IDbContextTransaction _transaction = null!;
    private ConvertedRow[] _convertedRows = [];
    private DirectRow[] _directRows = [];

    [GlobalSetup]
    public void GlobalSetup()
    {
        _connection = new DuckDBConnection("DataSource=:memory:");
        _connection.Open();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE converted_rows (id INTEGER, status INTEGER, quantity INTEGER, score BIGINT);
                CREATE TABLE direct_rows (id INTEGER, status INTEGER, quantity INTEGER, score BIGINT);
                """;
            command.ExecuteNonQuery();
        }

        _convertedContext = new ConvertedContext(
            new DbContextOptionsBuilder<ConvertedContext>()
                .UseDuckDB(_connection, contextOwnsConnection: false)
                .Options);
        _directContext = new DirectContext(
            new DbContextOptionsBuilder<DirectContext>()
                .UseDuckDB(_connection, contextOwnsConnection: false)
                .Options);
        _convertedRows = Enumerable.Range(1, RowCount)
            .Select(
                static id => new ConvertedRow
                {
                    Id = id,
                    Status = (RowStatus)(id % 3),
                    Quantity = new RowQuantity(id),
                    Score = new RowScore(id * 10L)
                })
            .ToArray();
        _directRows = Enumerable.Range(1, RowCount)
            .Select(
                static id => new DirectRow
                {
                    Id = id,
                    Status = id % 3,
                    Quantity = id,
                    Score = id * 10L
                })
            .ToArray();
        _convertedContext.BulkInsert(Array.Empty<ConvertedRow>());
        _directContext.BulkInsert(Array.Empty<DirectRow>());
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _convertedContext.Dispose();
        _directContext.Dispose();
        _connection.Dispose();
    }

    [IterationSetup(Target = nameof(ThreeValueConverters))]
    public void SetupConverted()
        => _transaction = _convertedContext.Database.BeginTransaction();

    [IterationSetup(Target = nameof(ProviderShapedProperties))]
    public void SetupDirect()
        => _transaction = _directContext.Database.BeginTransaction();

    [IterationCleanup]
    public void IterationCleanup()
    {
        _transaction.Rollback();
        _transaction.Dispose();
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = RowCount)]
    public int ThreeValueConverters()
        => _convertedContext.BulkInsert(_convertedRows);

    [Benchmark(OperationsPerInvoke = RowCount)]
    public int ProviderShapedProperties()
        => _directContext.BulkInsert(_directRows);

    private sealed class ConvertedContext(DbContextOptions<ConvertedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ConvertedRow>(
                entity =>
                {
                    entity.ToTable("converted_rows");
                    entity.HasKey(row => row.Id);
                    entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
                    entity.Property(row => row.Status).HasColumnName("status").HasConversion<int>();
                    entity.Property(row => row.Quantity).HasColumnName("quantity")
                        .HasConversion(value => value.Value, value => new RowQuantity(value));
                    entity.Property(row => row.Score).HasColumnName("score")
                        .HasConversion(value => value.Value, value => new RowScore(value));
                });
        }
    }

    private sealed class DirectContext(DbContextOptions<DirectContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DirectRow>(
                entity =>
                {
                    entity.ToTable("direct_rows");
                    entity.HasKey(row => row.Id);
                    entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
                    entity.Property(row => row.Status).HasColumnName("status");
                    entity.Property(row => row.Quantity).HasColumnName("quantity");
                    entity.Property(row => row.Score).HasColumnName("score");
                });
        }
    }

    private sealed class ConvertedRow
    {
        public int Id { get; set; }
        public RowStatus Status { get; set; }
        public RowQuantity Quantity { get; set; }
        public RowScore Score { get; set; }
    }

    private sealed class DirectRow
    {
        public int Id { get; set; }
        public int Status { get; set; }
        public int Quantity { get; set; }
        public long Score { get; set; }
    }

    private enum RowStatus
    {
        Pending,
        Active,
        Complete
    }

    private readonly record struct RowQuantity(int Value);

    private readonly record struct RowScore(long Value);
}

/// <summary>
///     Isolates the managed candidates from the review document before they are placed on provider hot paths.
/// </summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class PerformanceReviewManagedCandidateBenchmarks
{
    private const int OperationCount = 100_000;
    private const int ArrayOperationCount = 10_000;
    private const int PlannerCellCount = 512;

    private readonly Func<object, int> _objectAccessor = static value => ((ManagedRow)value).Value;
    private readonly Func<ManagedRow, int> _typedAccessor = static value => value.Value;
    private readonly Func<object?, object?> _untypedConverter = static value => (long)(int)value!;
    private readonly Func<string, int?>[] _typeRules =
    [
        static name => name.Contains("INT", StringComparison.OrdinalIgnoreCase) ? 1 : null,
        static name => name.Contains("CHAR", StringComparison.OrdinalIgnoreCase) ? 2 : null,
        static name => name.Contains("BLOB", StringComparison.OrdinalIgnoreCase) ? 3 : null,
        static name => name.Contains("REAL", StringComparison.OrdinalIgnoreCase) ? 4 : null
    ];
    private readonly ManagedRow _row = new() { Value = 42 };

    [Benchmark(Baseline = true, OperationsPerInvoke = OperationCount)]
    [BenchmarkCategory("ValueConverter")]
    public long BoxedValueConverter()
    {
        long total = 0;
        for (var i = 0; i < OperationCount; i++)
        {
            total += (long)_untypedConverter(i)!;
        }

        return total;
    }

    [Benchmark(OperationsPerInvoke = OperationCount)]
    [BenchmarkCategory("ValueConverter")]
    public long TypedValueConverter()
    {
        long total = 0;
        for (var i = 0; i < OperationCount; i++)
        {
            total += i;
        }

        return total;
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = OperationCount)]
    [BenchmarkCategory("EntityDelegate")]
    public int UntypedEntityDelegate()
    {
        var total = 0;
        for (var i = 0; i < OperationCount; i++)
        {
            total += _objectAccessor(_row);
        }

        return total;
    }

    [Benchmark(OperationsPerInvoke = OperationCount)]
    [BenchmarkCategory("EntityDelegate")]
    public int TypedEntityDelegate()
    {
        var total = 0;
        for (var i = 0; i < OperationCount; i++)
        {
            total += _typedAccessor(_row);
        }

        return total;
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = OperationCount)]
    [BenchmarkCategory("AffinityRules")]
    public int AffinityRulesLinq()
    {
        var total = 0;
        for (var i = 0; i < OperationCount; i++)
        {
            total += _typeRules.Select(rule => rule("VARCHAR")).FirstOrDefault(result => result is not null) ?? 0;
        }

        return total;
    }

    [Benchmark(OperationsPerInvoke = OperationCount)]
    [BenchmarkCategory("AffinityRules")]
    public int AffinityRulesLoop()
    {
        var total = 0;
        for (var i = 0; i < OperationCount; i++)
        {
            foreach (var rule in _typeRules)
            {
                if (rule("VARCHAR") is { } result)
                {
                    total += result;
                    break;
                }
            }
        }

        return total;
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = ArrayOperationCount)]
    [BenchmarkCategory("PlannerArrays")]
    public int AllocatePlannerArrays()
    {
        var total = 0;
        for (var i = 0; i < ArrayOperationCount; i++)
        {
            var values = new object?[PlannerCellCount];
            values[0] = _row;
            total += values.Length;
        }

        return total;
    }

    [Benchmark(OperationsPerInvoke = ArrayOperationCount)]
    [BenchmarkCategory("PlannerArrays")]
    public int RentPlannerArrays()
    {
        var total = 0;
        for (var i = 0; i < ArrayOperationCount; i++)
        {
            var values = ArrayPool<object?>.Shared.Rent(PlannerCellCount);
            values[0] = _row;
            total += values.Length;
            ArrayPool<object?>.Shared.Return(values, clearArray: true);
        }

        return total;
    }

    private sealed class ManagedRow
    {
        public int Value { get; set; }
    }
}

/// <summary>
///     Verifies and measures direct DuckDB.NET binding of an <see cref="IReadOnlyList{T}" /> value.
/// </summary>
[MemoryDiagnoser]
public class ReadOnlyListParameterBenchmarks
{
    private DuckDBConnection _connection = null!;
    private IReadOnlyList<int> _ids = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _connection = new DuckDBConnection("DataSource=:memory:");
        _connection.Open();
        using var command = _connection.CreateCommand();
        command.CommandText = "CREATE TABLE array_rows AS SELECT id::INTEGER AS id FROM range(1, 10_001) AS rows(id);";
        command.ExecuteNonQuery();
        _ids = new ReadOnlyCollection<int>(Enumerable.Range(1, 500).ToList());
        var count = Execute(_ids);
        if (count != 500)
        {
            throw new InvalidOperationException($"Direct IReadOnlyList binding returned {count} rows instead of 500.");
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
        => _connection.Dispose();

    [Benchmark(Baseline = true)]
    public long DefensiveListCopy()
        => Execute(_ids.ToList());

    [Benchmark]
    public long DirectReadOnlyList()
        => Execute(_ids);

    private long Execute(object values)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM array_rows WHERE id = ANY($ids);";
        command.Parameters.Add(new DuckDBParameter("ids", values));
        return Convert.ToInt64(command.ExecuteScalar());
    }
}

/// <summary>
///     Quantifies the upper-bound saving from caching an already-verified catalog attachment.
/// </summary>
[MemoryDiagnoser]
public class AttachedCatalogProbeBenchmarks
{
    private DuckDBConnection _connection = null!;
    private readonly string _cachedType = "duckdb";

    [GlobalSetup]
    public void GlobalSetup()
    {
        _connection = new DuckDBConnection("DataSource=:memory:");
        _connection.Open();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
        => _connection.Dispose();

    [Benchmark(Baseline = true)]
    public string ProbeDuckDbDatabases()
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT type FROM duckdb_databases() WHERE database_name = $catalog_name LIMIT 1;";
        command.Parameters.Add(new DuckDBParameter("catalog_name", "memory"));
        return Convert.ToString(command.ExecuteScalar())!;
    }

    [Benchmark]
    public string ReadCachedVerification()
        => _cachedType;
}