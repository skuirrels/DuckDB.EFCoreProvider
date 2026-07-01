using DuckDB.EFCoreProvider.Diagnostics.Internal;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Microsoft.EntityFrameworkCore;

public class LoggingDuckDBTest : LoggingRelationalTestBase<DuckDBDbContextOptionsBuilder, DuckDBOptionsExtension>
{
    protected class AmbientTransactionWarningContext(DbContextOptionsBuilder optionsBuilder) : DbContext(optionsBuilder.Options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Animal>();
    }

    protected override DbContextOptionsBuilder CreateOptionsBuilder(
        IServiceCollection services,
        Action<RelationalDbContextOptionsBuilder<DuckDBDbContextOptionsBuilder, DuckDBOptionsExtension>> relationalAction)
        => new DbContextOptionsBuilder()
            .UseInternalServiceProvider(services.AddEntityFrameworkDuckDB().BuildServiceProvider(validateScopes: true))
            .UseDuckDB("Data Source=LoggingDuckDBTest.db", relationalAction);

    protected override TestLogger CreateTestLogger()
        => new TestLogger<DuckDBLoggingDefinitions>();

    protected override string ProviderName
        => "DuckDB.EFCoreProvider";

    protected override string ProviderVersion
        => typeof(DuckDBOptionsExtension).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
}
