using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class FileSearchPathTests : DuckDBTestBase
{
    private SearchContext CreateContext(string? fileSearchPath)
        => new(FileOptions<SearchContext>(duckdb =>
        {
            if (fileSearchPath is not null)
            {
                duckdb.FileSearchPath(fileSearchPath);
            }
        }));

    private static string CurrentFileSearchPath(DbContext context)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT current_setting('file_search_path')";
            return (string)command.ExecuteScalar()!;
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    [ConditionalFact]
    public void FileSearchPath_is_applied_to_the_connection()
    {
        using var context = CreateContext("/tmp/duckef-data");
        context.Database.EnsureCreated();

        Assert.Equal("/tmp/duckef-data", CurrentFileSearchPath(context));
    }

    [ConditionalFact]
    public void Multiple_comma_separated_paths_are_applied()
    {
        using var context = CreateContext("/tmp/a,/tmp/b");
        context.Database.EnsureCreated();

        Assert.Equal("/tmp/a,/tmp/b", CurrentFileSearchPath(context));
    }

    [ConditionalFact]
    public void Default_is_unchanged_when_not_configured()
    {
        using var configured = CreateContext("/tmp/duckef-data");
        configured.Database.EnsureCreated();

        using var notConfigured = CreateContext(fileSearchPath: null);

        Assert.NotEqual("/tmp/duckef-data", CurrentFileSearchPath(notConfigured));
    }

    [ConditionalFact]
    public void FileSearchPath_sets_the_option()
    {
        using var context = CreateContext("/data");
        var extension = context.GetService<IDbContextOptions>()
            .FindExtension<DuckDBOptionsExtension>();

        Assert.Equal("/data", extension!.FileSearchPath);
    }

    [ConditionalFact]
    public void FileSearchPath_rejects_null_or_whitespace()
    {
        Assert.Throws<ArgumentException>(() => CreateContext(""));
        Assert.Throws<ArgumentException>(() => CreateContext("   "));
    }

    private sealed class SearchContext(DbContextOptions<SearchContext> options) : DbContext(options)
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
