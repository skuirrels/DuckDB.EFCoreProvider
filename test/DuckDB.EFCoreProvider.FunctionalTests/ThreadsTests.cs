using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class ThreadsTests : DuckDBTestBase
{
    private ThreadsContext CreateContext(int? threads)
        => new(FileOptions<ThreadsContext>(duckdb =>
        {
            if (threads is not null)
            {
                duckdb.Threads(threads.Value);
            }
        }));

    [ConditionalFact]
    public void Threads_is_applied_when_the_connection_opens()
    {
        using var context = CreateContext(2);

        context.Database.OpenConnection();
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT current_setting('threads')";

        Assert.Equal(2L, command.ExecuteScalar());
    }

    [ConditionalFact]
    public void Threads_sets_the_option()
    {
        using var context = CreateContext(3);
        var extension = context.GetService<IDbContextOptions>()
            .FindExtension<DuckDBOptionsExtension>();

        Assert.Equal(3, extension!.Threads);
    }

    [ConditionalFact]
    public void Threads_is_applied_to_provider_owned_read_only_connections()
    {
        using var context = CreateContext(4);

        // A read-only connection cannot create the database file. Close the only writable
        // connection first so the read-only open creates a new DuckDB database instance;
        // the global threads setting must therefore be propagated to that connection.
        context.Database.OpenConnection();
        context.Database.CloseConnection();

        using var readOnlyConnection = context.GetService<IDuckDBRelationalConnection>()
            .CreateReadOnlyConnection();

        readOnlyConnection.Open();
        using var command = readOnlyConnection.DbConnection.CreateCommand();
        command.CommandText = "SELECT current_setting('threads')";

        Assert.Equal(4L, command.ExecuteScalar());
    }

    [Fact]
    public void Threads_rejects_non_positive_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateContext(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateContext(-1));
    }

    private sealed class ThreadsContext(DbContextOptions<ThreadsContext> options) : DbContext(options);
}