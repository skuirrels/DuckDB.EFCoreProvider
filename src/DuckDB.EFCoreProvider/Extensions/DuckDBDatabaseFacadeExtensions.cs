using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     DuckDB specific extension methods for <see cref="DbContext.Database" />.
/// </summary>
public static class DuckDBDatabaseFacadeExtensions
{
    /// <summary>
    ///     Returns <see langword="true" /> if the database provider currently in use is the DuckDB provider.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This method can only be used after the <see cref="DbContext" /> has been configured because
    ///         it is only then that the provider is known. This means that this method cannot be used
    ///         in <see cref="DbContext.OnConfiguring" /> because this is where application code sets the
    ///         provider to use as part of configuring the context.
    ///     </para>
    /// </remarks>
    /// <param name="database">The facade from <see cref="DbContext.Database" />.</param>
    /// <returns><see langword="true" /> if DuckDB is being used; <see langword="false" /> otherwise.</returns>
    public static bool IsDuckDB(this DatabaseFacade database)
        => database.ProviderName == typeof(DuckDBOptionsExtension).Assembly.GetName().Name;
}
