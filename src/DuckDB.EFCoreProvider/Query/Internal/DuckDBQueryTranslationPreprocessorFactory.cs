using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBQueryTranslationPreprocessorFactory : RelationalQueryTranslationPreprocessorFactory
{
    public DuckDBQueryTranslationPreprocessorFactory(
        QueryTranslationPreprocessorDependencies dependencies,
        RelationalQueryTranslationPreprocessorDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    /// <inheritdoc />
    public override QueryTranslationPreprocessor Create(QueryCompilationContext queryCompilationContext)
    {
        return new DuckDBQueryTranslationPreprocessor(
            Dependencies,
            RelationalDependencies,
            queryCompilationContext);
    }
}
