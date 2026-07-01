using DuckDB.EFCoreProvider.Infrastructure;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.NTS.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DuckDB.EFCoreProvider.NTS.Extensions;

public static class DuckDBNetTopologySuiteDbContextOptionsBuilderExtensions
{
    public static DuckDBDbContextOptionsBuilder UseNetTopologySuite(this DuckDBDbContextOptionsBuilder optionsBuilder)
    {
        var coreOptionsBuilder = ((IRelationalDbContextOptionsBuilderInfrastructure)optionsBuilder).OptionsBuilder;
        var infrastructure = (IDbContextOptionsBuilderInfrastructure)coreOptionsBuilder;
#pragma warning disable EF1001 // Internal EF Core API usage.
        // #20566
        var DuckDBExtension = coreOptionsBuilder.Options.FindExtension<DuckDBOptionsExtension>()
                              ?? new DuckDBOptionsExtension();
        var ntsExtension = coreOptionsBuilder.Options.FindExtension<DuckDBNetTopologySuiteOptionsExtension>()
                           ?? new DuckDBNetTopologySuiteOptionsExtension();

        infrastructure.AddOrUpdateExtension(DuckDBExtension.WithLoadSpatialite(true));
#pragma warning restore EF1001 // Internal EF Core API usage.
        infrastructure.AddOrUpdateExtension(ntsExtension);

        return optionsBuilder;
    }
}
