using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DuckDB.EFCoreProvider.Infrastructure.Internal;

/// <summary>
///     Represents DuckLake backend options that must remain constant within an EF Core internal service provider.
/// </summary>
public interface IDuckLakeSingletonOptions : ISingletonOptions
{
    /// <summary>Whether the provider is targeting an attached DuckLake catalog.</summary>
    bool IsDuckLake { get; }
}