using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DuckDB.EFCoreProvider.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs.
/// </summary>
public sealed class DuckLakeSingletonOptions : IDuckLakeSingletonOptions
{
    /// <inheritdoc />
    public bool IsDuckLake { get; private set; }

    /// <inheritdoc />
    public void Initialize(IDbContextOptions options)
        => IsDuckLake = options.FindExtension<DuckDBOptionsExtension>()?.DuckLakeOptions is not null;

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
        if ((options.FindExtension<DuckDBOptionsExtension>()?.DuckLakeOptions is not null) != IsDuckLake)
        {
            throw new InvalidOperationException(
                "DuckDB and DuckLake contexts cannot share an explicitly supplied EF Core internal service provider. "
                + "Use separate service providers, or allow EF Core to create them automatically.");
        }
    }
}