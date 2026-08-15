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

    /// <summary>
    ///     Whether simple string searches use case-insensitive DuckDB matching.
    /// </summary>
    bool CaseInsensitiveStringSearchesEnabled => false;

    /// <summary>Whether commands execute against a remote DuckDB server through Quack.</summary>
    bool IsQuack => false;
}