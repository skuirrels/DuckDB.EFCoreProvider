using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBCompiledQueryCacheKeyGenerator : RelationalCompiledQueryCacheKeyGenerator
{
    public DuckDBCompiledQueryCacheKeyGenerator(
        CompiledQueryCacheKeyGeneratorDependencies dependencies,
        RelationalCompiledQueryCacheKeyGeneratorDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    /// <inheritdoc />
    public override object GenerateCacheKey(Expression query, bool async)
        => new DuckDBCompiledQueryCacheKey(
            GenerateCacheKeyCore(query, async),
            RelationalDependencies.ContextOptions.FindExtension<DuckDBOptionsExtension>()?.ReverseNullOrdering ?? false);

    private struct DuckDBCompiledQueryCacheKey(
        RelationalCompiledQueryCacheKey relationalCompiledQueryCacheKey,
        bool reverseNullOrdering)
    {
        private readonly RelationalCompiledQueryCacheKey _relationalCompiledQueryCacheKey = relationalCompiledQueryCacheKey;
        private readonly bool _reverseNullOrdering = reverseNullOrdering;

        public override bool Equals(object? obj)
            => !(obj is null)
               && obj is DuckDBCompiledQueryCacheKey key
               && Equals(key);

        private bool Equals(DuckDBCompiledQueryCacheKey other)
            => _relationalCompiledQueryCacheKey.Equals(other._relationalCompiledQueryCacheKey)
               && _reverseNullOrdering == other._reverseNullOrdering;

        public override int GetHashCode()
            => HashCode.Combine(_relationalCompiledQueryCacheKey, _reverseNullOrdering);
    }
}
