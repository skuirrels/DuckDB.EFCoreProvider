using DuckDB.EFCoreProvider.Diagnostics;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Update.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class BulkInsertBatchingTests : DuckDBTestBase
{
    private BatchingContext CreateContext(bool enableBatching)
        => new(FileOptions<BatchingContext>(duckdb =>
        {
            if (enableBatching)
            {
                duckdb.EnableBulkInsertBatching();
            }
        }));

    private BatchingContext CreateContext(DbCommandInterceptor interceptor)
        => new(new DbContextOptionsBuilder<BatchingContext>(
                FileOptions<BatchingContext>(duckdb => duckdb.EnableBulkInsertBatching()))
            .AddInterceptors(interceptor)
            .Options);

    private BatchingContext CreateWideContext(DbCommandInterceptor interceptor, ILoggerFactory loggerFactory)
        => new(new DbContextOptionsBuilder<BatchingContext>(
                FileOptions<BatchingContext>(duckdb => duckdb.EnableBulkInsertBatching().MaxBatchSize(1_000)))
            .AddInterceptors(interceptor)
            .UseLoggerFactory(loggerFactory)
            .Options);

    [ConditionalFact]
    public void SaveChanges_with_batching_persists_all_rows_with_correct_values()
    {
        using (var context = CreateContext(enableBatching: true))
        {
            context.Database.EnsureCreated();

            context.AddRange(
                Enumerable.Range(1, 500)
                    .Select(i => new ExplicitKeyRow { Id = i, Name = $"row-{i}", Value = i * 1.5, Active = i % 2 == 0 }));

            context.SaveChanges();
        }

        using (var context = CreateContext(enableBatching: true))
        {
            Assert.Equal(500, context.ExplicitKeyRows.Count());

            var first = context.ExplicitKeyRows.Single(x => x.Id == 1);
            Assert.Equal("row-1", first.Name);
            Assert.Equal(1.5, first.Value);
            Assert.False(first.Active);

            var last = context.ExplicitKeyRows.Single(x => x.Id == 500);
            Assert.Equal("row-500", last.Name);
            Assert.True(last.Active);
        }
    }

    [ConditionalFact]
    public void SaveChanges_with_batching_correlates_store_generated_keys()
    {
        var rows = Enumerable.Range(0, 50)
            .Select(i => new GeneratedKeyRow { Name = $"g{i}" })
            .ToList();

        using var context = CreateContext(enableBatching: true);
        context.Database.EnsureCreated();

        context.AddRange(rows);
        context.SaveChanges();

        // Every row received a distinct, populated, store-generated key, correlated back to the right entity.
        Assert.All(rows, r => Assert.True(r.Id > 0));
        Assert.Equal(rows.Count, rows.Select(r => r.Id).Distinct().Count());

        foreach (var row in rows)
        {
            Assert.Equal(row.Name, context.GeneratedKeyRows.Single(x => x.Id == row.Id).Name);
        }
    }

    [ConditionalFact]
    public async Task SaveChangesAsync_with_batching_persists_rows()
    {
        await using var context = CreateContext(enableBatching: true);
        await context.Database.EnsureCreatedAsync();

        context.AddRange(
            Enumerable.Range(1, 250).Select(i => new ExplicitKeyRow { Id = i, Name = $"r{i}", Value = i }));

        await context.SaveChangesAsync();

        Assert.Equal(250, await context.ExplicitKeyRows.CountAsync());
    }

    [ConditionalFact]
    public void SaveChanges_with_batching_preserves_planned_sql_shape()
    {
        using (var context = CreateContext(enableBatching: true))
        {
            context.Database.EnsureCreated();
        }

        var interceptor = new CommandCaptureInterceptor();
        using (var context = CreateContext(interceptor))
        {
            context.AddRange(
                new ExplicitKeyRow { Id = 1, Name = "one", Value = 1.5, Active = true },
                new ExplicitKeyRow { Id = 2, Name = "two", Value = 2.5, Active = false });
            context.SaveChanges();
        }

        var sql = Assert.Single(interceptor.CommandTexts.Where(
            commandText => commandText.StartsWith("INSERT ", StringComparison.Ordinal)));
        Assert.StartsWith("INSERT INTO \"ExplicitKeyRows\"", sql);
        Assert.Contains("VALUES (", sql);
        Assert.Contains($"),{Environment.NewLine}(", sql);
        Assert.DoesNotContain("RETURNING", sql);
    }

    [ConditionalFact]
    public void Bulk_insert_planner_rejects_an_empty_command_run()
    {
        Assert.False(DuckDBBulkInsertPlanner.TryCreate([], out var plan));
        Assert.Null(plan);
    }

    [ConditionalFact]
    public void SaveChanges_with_batching_splits_inserts_with_different_write_shapes()
    {
        using (var context = CreateContext(enableBatching: true))
        {
            context.Database.EnsureCreated();
        }

        var interceptor = new CommandCaptureInterceptor();
        using (var context = CreateContext(interceptor))
        {
            context.AddRange(
                new DefaultedValueRow { Id = 1 },
                new DefaultedValueRow { Id = 2, Value = 42 });
            context.SaveChanges();
        }

        var sql = string.Join(
            Environment.NewLine,
            interceptor.CommandTexts.Where(
                commandText => commandText.Contains("INSERT INTO \"DefaultedValueRows\"", StringComparison.Ordinal)));
        Assert.Equal(2, sql.Split("INSERT INTO \"DefaultedValueRows\"", StringSplitOptions.None).Length - 1);

        using var verification = CreateContext(enableBatching: true);
        Assert.Equal(7, verification.DefaultedValueRows.Single(row => row.Id == 1).Value);
        Assert.Equal(42, verification.DefaultedValueRows.Single(row => row.Id == 2).Value);
    }

    [ConditionalFact]
    public void SaveChanges_with_batching_splits_wide_rows_at_the_cell_limit_and_logs_the_decision()
    {
        var interceptor = new CommandCaptureInterceptor();
        var loggerProvider = new CaptureLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(
            builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(loggerProvider));
        using var context = CreateWideContext(interceptor, loggerFactory);
        context.Database.EnsureCreated();
        interceptor.CommandTexts.Clear();
        loggerProvider.Events.Clear();

        context.AddRange(Enumerable.Range(1, 700).Select(i => new WideRow
        {
            Id = i,
            C01 = i,
            C02 = i,
            C03 = i,
            C04 = i,
            C05 = i,
            C06 = i,
            C07 = i,
            C08 = i,
            C09 = i,
            C10 = i,
            C11 = i,
            C12 = i,
            C13 = i,
            C14 = i,
            C15 = i,
        }));

        Assert.Equal(700, context.SaveChanges());

        Assert.Equal(
            2,
            interceptor.CommandTexts.Count(
                commandText => commandText.StartsWith("INSERT INTO \"WideRows\"", StringComparison.Ordinal)));
        Assert.Contains(
            loggerProvider.Events,
            entry => entry.EventId == DuckDBEventId.SaveChangesBatch
                     && entry.Message.Contains("columns=16", StringComparison.Ordinal)
                     && entry.Message.Contains("cells=10016", StringComparison.Ordinal)
                     && entry.Message.Contains("reason=CellLimit", StringComparison.Ordinal));
        Assert.Contains(
            loggerProvider.Events,
            entry => entry.EventId == DuckDBEventId.SaveChangesBatch
                     && entry.Message.Contains("reason=Completed", StringComparison.Ordinal));
    }

    [ConditionalFact]
    public void Batching_is_disabled_by_default()
    {
        using var context = CreateContext(enableBatching: false);
        var extension = context.GetService<IDbContextOptions>()
            .FindExtension<DuckDBOptionsExtension>();

        Assert.NotNull(extension);
        Assert.False(extension!.BulkInsertBatching);
    }

    [ConditionalFact]
    public void EnableBulkInsertBatching_sets_the_option()
    {
        using var context = CreateContext(enableBatching: true);
        var extension = context.GetService<IDbContextOptions>()
            .FindExtension<DuckDBOptionsExtension>();

        Assert.NotNull(extension);
        Assert.True(extension!.BulkInsertBatching);
    }

    private sealed class BatchingContext(DbContextOptions<BatchingContext> options) : DbContext(options)
    {
        public DbSet<ExplicitKeyRow> ExplicitKeyRows => Set<ExplicitKeyRow>();
        public DbSet<GeneratedKeyRow> GeneratedKeyRows => Set<GeneratedKeyRow>();
        public DbSet<DefaultedValueRow> DefaultedValueRows => Set<DefaultedValueRow>();
        public DbSet<WideRow> WideRows => Set<WideRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ExplicitKeyRow>().Property(e => e.Id).ValueGeneratedNever();
            modelBuilder.Entity<WideRow>().Property(e => e.Id).ValueGeneratedNever();
            modelBuilder.Entity<DefaultedValueRow>(entity =>
            {
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.Value).HasDefaultValue(7);
            });
        }
    }

    private sealed class ExplicitKeyRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public double Value { get; set; }
        public bool Active { get; set; }
    }

    private sealed class GeneratedKeyRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class DefaultedValueRow
    {
        public int Id { get; set; }
        public int Value { get; set; }
    }

    private sealed class WideRow
    {
        public int Id { get; set; }
        public int C01 { get; set; }
        public int C02 { get; set; }
        public int C03 { get; set; }
        public int C04 { get; set; }
        public int C05 { get; set; }
        public int C06 { get; set; }
        public int C07 { get; set; }
        public int C08 { get; set; }
        public int C09 { get; set; }
        public int C10 { get; set; }
        public int C11 { get; set; }
        public int C12 { get; set; }
        public int C13 { get; set; }
        public int C14 { get; set; }
        public int C15 { get; set; }
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> CommandTexts { get; } = [];

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            CommandTexts.Add(command.CommandText);
            return result;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            CommandTexts.Add(command.CommandText);
            return result;
        }
    }

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        public List<(EventId EventId, string Message)> Events { get; } = [];

        public ILogger CreateLogger(string categoryName)
            => new CaptureLogger(Events);

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(List<(EventId EventId, string Message)> events) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel)
                => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => events.Add((eventId, formatter(state, exception)));
        }
    }
}