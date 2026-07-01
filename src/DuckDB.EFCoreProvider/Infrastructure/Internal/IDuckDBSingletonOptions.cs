using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DuckDB.EFCoreProvider.Infrastructure.Internal;

/// <summary>
///     Represents options for DuckDB that can only be set at the <see cref="IServiceProvider" /> singleton level.
/// </summary>
public interface IDuckDBSingletonOptions : ISingletonOptions
{
    /// <summary>
    ///     Whether reverse <see langword="null" /> ordering is enabled.
    /// </summary>
    bool ReverseNullOrderingEnabled { get; }
}
