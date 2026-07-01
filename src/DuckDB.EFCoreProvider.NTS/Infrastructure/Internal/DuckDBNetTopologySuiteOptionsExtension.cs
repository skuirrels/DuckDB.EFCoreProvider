using DuckDB.EFCoreProvider.NTS.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DuckDB.EFCoreProvider.NTS.Infrastructure.Internal;

public class DuckDBNetTopologySuiteOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public void ApplyServices(IServiceCollection services)
    {
        services.AddEntityFrameworkDuckDBNetTopologySuite();
    }

    public void Validate(IDbContextOptions options)
    {
    }

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo;

        public override string LogFragment => "using NetTopologySuite ";

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["DuckDB.NTS"] = "1";

        public override int GetServiceProviderHashCode() => 0;
    }
}
