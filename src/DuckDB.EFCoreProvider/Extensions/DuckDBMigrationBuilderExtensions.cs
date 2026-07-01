using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     DuckDB specific extension methods for <see cref="MigrationBuilder" />.
/// </summary>
public static class DuckDBMigrationBuilderExtensions
{
    /// <summary>
    ///     Returns <see langword="true" /> if the database provider currently in use is the DuckDB provider.
    /// </summary>
    /// <param name="migrationBuilder">
    ///     The migrationBuilder from the parameters on <see cref="Migration.Up(MigrationBuilder)" /> or
    ///     <see cref="Migration.Down(MigrationBuilder)" />.
    /// </param>
    /// <returns><see langword="true" /> if DuckDB is being used; <see langword="false" /> otherwise.</returns>
    public static bool IsDuckDB(this MigrationBuilder migrationBuilder)
        => string.Equals(
            migrationBuilder.ActiveProvider,
            typeof(DuckDBOptionsExtension).Assembly.GetName().Name,
            StringComparison.Ordinal);
}
