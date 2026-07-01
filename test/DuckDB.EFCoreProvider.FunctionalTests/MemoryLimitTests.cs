using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class MemoryLimitTests : DuckDBTestBase
{
    private MemContext CreateContext(string? memoryLimit)
        => new(FileOptions<MemContext>(duckdb =>
        {
            if (memoryLimit is not null)
            {
                duckdb.MemoryLimit(memoryLimit);
            }
        }));

    private static string CurrentMemoryLimit(DbContext context)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT current_setting('memory_limit')";
            return (string)command.ExecuteScalar()!;
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    [ConditionalFact]
    public void MemoryLimit_is_applied_to_the_connection()
    {
        using var context = CreateContext("256MiB");
        context.Database.EnsureCreated();

        var setting = CurrentMemoryLimit(context);

        // DuckDB reports the configured limit (binary units round-trip exactly).
        Assert.Equal("256.0 MiB", setting);
    }

    [ConditionalFact]
    public void Default_memory_limit_is_unchanged_when_not_configured()
    {
        using var configured = CreateContext("128MiB");
        configured.Database.EnsureCreated();
        var configuredSetting = CurrentMemoryLimit(configured);

        using var notConfigured = CreateContext(memoryLimit: null);
        var defaultSetting = CurrentMemoryLimit(notConfigured);

        // The explicit tiny limit is applied; the default (80% of RAM) is left untouched (and is not 128 MiB).
        Assert.Equal("128.0 MiB", configuredSetting);
        Assert.NotEqual("128.0 MiB", defaultSetting);
    }

    [ConditionalFact]
    public void MemoryLimit_sets_the_option()
    {
        using var context = CreateContext("4GB");
        var extension = context.GetService<IDbContextOptions>()
            .FindExtension<DuckDBOptionsExtension>();

        Assert.Equal("4GB", extension!.MemoryLimit);
    }

    [ConditionalFact]
    public void MemoryLimit_rejects_null_or_whitespace()
    {
        Assert.Throws<ArgumentException>(() => CreateContext(""));
        Assert.Throws<ArgumentException>(() => CreateContext("   "));
    }

    private sealed class MemContext(DbContextOptions<MemContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Row>().Property(e => e.Id).ValueGeneratedNever();
    }

    private sealed class Row
    {
        public int Id { get; set; }
    }
}
