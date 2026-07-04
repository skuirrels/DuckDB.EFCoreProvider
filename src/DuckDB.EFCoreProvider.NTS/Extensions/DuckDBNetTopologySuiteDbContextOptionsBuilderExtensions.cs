using DuckDB.EFCoreProvider.Infrastructure;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.NTS.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DuckDB.EFCoreProvider.NTS.Extensions;

/// <summary>
///     NetTopologySuite-specific extension methods for <see cref="DuckDBDbContextOptionsBuilder" />.
/// </summary>
public static class DuckDBNetTopologySuiteDbContextOptionsBuilderExtensions
{
    /// <summary>
    ///     Enables NetTopologySuite spatial support for the DuckDB provider, mapping <c>Geometry</c>-derived
    ///     CLR properties to DuckDB's native <c>GEOMETRY</c> column type and loading DuckDB's <c>spatial</c>
    ///     extension.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the DuckDB provider.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static DuckDBDbContextOptionsBuilder UseNetTopologySuite(this DuckDBDbContextOptionsBuilder optionsBuilder)
    {
        var coreOptionsBuilder = ((IRelationalDbContextOptionsBuilderInfrastructure)optionsBuilder).OptionsBuilder;
        var infrastructure = (IDbContextOptionsBuilderInfrastructure)coreOptionsBuilder;
#pragma warning disable EF1001 // Internal EF Core API usage.
        // Mirrors Microsoft.Data.Sqlite.NTS's UseNetTopologySuite(), which merges into the provider's own
        // options extension to flip its "load the spatial extension" flag (see dotnet/efcore#20566).
        var duckDBExtension = coreOptionsBuilder.Options.FindExtension<DuckDBOptionsExtension>()
                              ?? new DuckDBOptionsExtension();
        var ntsExtension = coreOptionsBuilder.Options.FindExtension<DuckDBNetTopologySuiteOptionsExtension>()
                           ?? new DuckDBNetTopologySuiteOptionsExtension();

        infrastructure.AddOrUpdateExtension(duckDBExtension.WithLoadSpatialite(true));
#pragma warning restore EF1001 // Internal EF Core API usage.
        infrastructure.AddOrUpdateExtension(ntsExtension);

        return optionsBuilder;
    }
}
