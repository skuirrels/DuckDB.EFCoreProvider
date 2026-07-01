using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class ProductionWriteDuckDBTest
{
    [Fact]
    public void Sequence_value_generation_uses_duckdb_nextval_literal_syntax()
    {
        using var context = CreateContext("Data Source=:memory:");
        var generator = context.GetService<IUpdateSqlGenerator>();

        Assert.Equal("SELECT nextval('\"orders_id_seq\"')", generator.GenerateNextSequenceValueOperation("orders_id_seq", null));
        Assert.Equal("nextval('\"app\".\"orders_id_seq\"')", generator.GenerateObtainNextSequenceValueOperation("orders_id_seq", "app"));
        Assert.Equal("SELECT nextval('\"tenant''s\".\"orders_id_seq\"')", generator.GenerateNextSequenceValueOperation("orders_id_seq", "tenant's"));
        Assert.Equal("SELECT nextval('\"app\".\"orders.id.seq\"')", generator.GenerateNextSequenceValueOperation("orders.id.seq", "app"));
        Assert.Equal("SELECT nextval('\"app\".\"orders\"\"id\"')", generator.GenerateNextSequenceValueOperation("orders\"id", "app"));
    }

    [Fact]
    public void Model_validation_rejects_auto_increment_on_non_integer_property()
    {
        using var context = CreateInvalidAutoIncrementContext();

        var exception = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Contains("DuckDB auto-increment value generation can only be configured for integer properties", exception.Message);
    }

    [Fact]
    public async Task SaveChanges_populates_auto_increment_keys_from_returning_clause()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            await using var context = CreateContext(databasePath);
            await context.Database.EnsureDeletedAsync();
            await context.Database.EnsureCreatedAsync();

            var first = new WriteEntity { Name = "first", UpdatedBy = "seed" };
            var second = new WriteEntity { Name = "second", UpdatedBy = "seed" };

            context.Entities.AddRange(first, second);
            await context.SaveChangesAsync();

            Assert.True(first.Id > 0);
            Assert.True(second.Id > first.Id);

            var persisted = await context.Entities
                .OrderBy(e => e.Id)
                .Select(e => new { e.Id, e.Name })
                .ToListAsync();

            Assert.Equal(
                [
                    new { first.Id, first.Name },
                    new { second.Id, second.Name }
                ],
                persisted);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public void Database_exists_check_does_not_create_missing_database_file()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            using var context = CreateContext(databasePath);
            var creator = context.GetService<IRelationalDatabaseCreator>();

            Assert.False(creator.Exists());
            Assert.False(File.Exists(databasePath));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task SaveChanges_throws_concurrency_exception_when_update_matches_no_rows()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            await using (var setup = CreateContext(databasePath))
            {
                await setup.Database.EnsureDeletedAsync();
                await setup.Database.EnsureCreatedAsync();
                setup.Entities.Add(new WriteEntity { Name = "original", UpdatedBy = "seed" });
                await setup.SaveChangesAsync();
            }

            await using var firstContext = CreateContext(databasePath);
            await using var secondContext = CreateContext(databasePath);

            var first = await firstContext.Entities.SingleAsync();
            var second = await secondContext.Entities.SingleAsync();

            first.Name = "changed";
            await firstContext.SaveChangesAsync();

            second.UpdatedBy = "late-writer";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task SaveChanges_throws_concurrency_exception_when_delete_matches_no_rows()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            await using (var setup = CreateContext(databasePath))
            {
                await setup.Database.EnsureDeletedAsync();
                await setup.Database.EnsureCreatedAsync();
                setup.Entities.Add(new WriteEntity { Name = "delete-me", UpdatedBy = "seed" });
                await setup.SaveChangesAsync();
            }

            await using var firstContext = CreateContext(databasePath);
            await using var secondContext = CreateContext(databasePath);

            var first = await firstContext.Entities.SingleAsync();
            var second = await secondContext.Entities.SingleAsync();

            firstContext.Entities.Remove(first);
            await firstContext.SaveChangesAsync();

            secondContext.Entities.Remove(second);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    private static WriteDbContext CreateContext(string databasePathOrConnectionString)
    {
        var connectionString = databasePathOrConnectionString.Contains('=', StringComparison.Ordinal)
            ? databasePathOrConnectionString
            : $"Data Source={databasePathOrConnectionString}";

        var options = new DbContextOptionsBuilder<WriteDbContext>()
            .UseDuckDB(connectionString)
            .Options;

        return new WriteDbContext(options);
    }

    private static InvalidAutoIncrementDbContext CreateInvalidAutoIncrementContext()
    {
        var options = new DbContextOptionsBuilder<InvalidAutoIncrementDbContext>()
            .UseDuckDB("Data Source=:memory:")
            .Options;

        return new InvalidAutoIncrementDbContext(options);
    }

    private static string CreateDatabasePath()
        => Path.Combine(Path.GetTempPath(), "duckdb-efcore-write-" + Guid.NewGuid().ToString("N") + ".duckdb");

    private static void DeleteDatabaseFiles(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        var fileName = Path.GetFileName(databasePath);

        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory, fileName + "*"))
        {
            File.Delete(path);
        }
    }

    private sealed class WriteDbContext(DbContextOptions<WriteDbContext> options) : DbContext(options)
    {
        public DbSet<WriteEntity> Entities => Set<WriteEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<WriteEntity>(entity =>
            {
                entity.ToTable("write_entities");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd().UseAutoIncrement();
                entity.Property(e => e.Name).IsRequired().IsConcurrencyToken();
                entity.Property(e => e.UpdatedBy).HasMaxLength(64);
            });
        }
    }

    private sealed class WriteEntity
    {
        public int Id { get; set; }

        public required string Name { get; set; }

        public string? UpdatedBy { get; set; }
    }

    private sealed class InvalidAutoIncrementDbContext(DbContextOptions<InvalidAutoIncrementDbContext> options) : DbContext(options)
    {
        public DbSet<InvalidAutoIncrementEntity> Entities => Set<InvalidAutoIncrementEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<InvalidAutoIncrementEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd().UseAutoIncrement();
            });
        }
    }

    private sealed class InvalidAutoIncrementEntity
    {
        public string Id { get; set; } = null!;
    }
}
