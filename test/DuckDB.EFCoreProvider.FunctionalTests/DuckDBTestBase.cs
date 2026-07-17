using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Base class for the provider's focused functional tests: owns a unique temporary file database, builds
///     options against it, and deletes the file on dispose.
/// </summary>
public abstract class DuckDBTestBase : IDisposable
{
    /// <summary>Path to this test's isolated DuckDB file database.</summary>
    protected string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"ddbtest_{Guid.NewGuid():N}.db");

    /// <summary>Builds options for a context backed by this test's file database.</summary>
    protected DbContextOptions<TContext> FileOptions<TContext>(Action<DuckDBDbContextOptionsBuilder>? configure = null)
        where TContext : DbContext
        => new DbContextOptionsBuilder<TContext>()
            .UseDuckDB($"DataSource={DbPath}", duckdb => configure?.Invoke(duckdb))
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

    public void Dispose()
    {
        if (File.Exists(DbPath))
        {
            File.Delete(DbPath);
        }

        GC.SuppressFinalize(this);
    }
}
