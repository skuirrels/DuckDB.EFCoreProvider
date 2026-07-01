using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Regression tests for <c>DuckDBDatabaseCreator.Exists()</c>, which reports existence of a file database
///     from the presence of the file itself, so that a database whose file exists but is currently held open
///     (the single-writer lock) is not misreported as "does not exist".
/// </summary>
public class DatabaseExistsTests : DuckDBTestBase
{
    private ExistsContext CreateContext()
        => new(FileOptions<ExistsContext>());

    private static bool Exists(DbContext context)
        => context.GetService<IRelationalDatabaseCreator>().Exists();

    [ConditionalFact]
    public void Exists_is_false_for_an_absent_database()
    {
        using var context = CreateContext();

        Assert.False(File.Exists(DbPath));
        Assert.False(Exists(context));
    }

    [ConditionalFact]
    public void Exists_is_true_for_a_present_database()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
        }

        Assert.True(File.Exists(DbPath));

        using var probe = CreateContext();
        Assert.True(Exists(probe));
    }

    [ConditionalFact]
    public void Exists_is_true_when_the_file_is_present_but_not_openable()
    {
        // A file that is present but cannot be opened as a database (here, garbage content) stands in for the
        // real motivation: a database whose file exists but is currently held by another writer process.
        // Existence is determined from the file's presence, not from a successful open — so this returns true,
        // whereas the previous open-probe implementation would have misreported it as "does not exist".
        File.WriteAllText(DbPath, "not a duckdb database");

        using var context = CreateContext();
        Assert.True(Exists(context));
    }

    [ConditionalFact]
    public void Exists_is_true_for_an_in_memory_database()
    {
        using var context = new ExistsContext(new DbContextOptionsBuilder<ExistsContext>()
            .UseDuckDB("DataSource=:memory:")
            .Options);

        Assert.True(Exists(context));
    }

    private sealed class ExistsContext(DbContextOptions<ExistsContext> options) : DbContext(options)
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
