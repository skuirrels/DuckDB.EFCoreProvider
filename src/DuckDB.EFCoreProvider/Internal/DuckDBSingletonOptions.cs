using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DuckDB.EFCoreProvider.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBSingletonOptions : IDuckDBSingletonOptions
{
    public virtual bool ReverseNullOrderingEnabled { get; private set; }

    /// <inheritdoc />
    public void Initialize(IDbContextOptions options)
    {
        var duckDbOptions = options.FindExtension<DuckDBOptionsExtension>() ?? new DuckDBOptionsExtension();

        ReverseNullOrderingEnabled = duckDbOptions.ReverseNullOrdering;
    }

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
        var duckDbOptions = options.FindExtension<DuckDBOptionsExtension>() ?? new DuckDBOptionsExtension();

        if (duckDbOptions.ReverseNullOrdering != ReverseNullOrderingEnabled)
        {
            throw new InvalidOperationException(
                $"A call was made to an option method ('{nameof(DuckDBOptionsExtension.ReverseNullOrdering)}') that"
                + " changed an option that must be constant within a service provider, but Entity Framework is not"
                + " building its own internal service provider. Either allow Entity Framework to build the service"
                + " provider by removing the call to 'UseInternalServiceProvider', or ensure that the configuration"
                + " does not change for all uses of a given service provider.");
        }
    }
}
