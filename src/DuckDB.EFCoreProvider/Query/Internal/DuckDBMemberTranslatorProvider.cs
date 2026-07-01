using DuckDB.EFCoreProvider.Query.ExpressionTranslators.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBMemberTranslatorProvider : RelationalMemberTranslatorProvider
{
    public DuckDBMemberTranslatorProvider(RelationalMemberTranslatorProviderDependencies dependencies, IRelationalTypeMappingSource typeMappingSource)
        : base(dependencies)
    {
        AddTranslators([
            new DuckDBStringMemberTranslator(dependencies.SqlExpressionFactory),
            new DuckDBDateOnlyMemberTranslator(dependencies.SqlExpressionFactory),
            new DuckDBDateTimeMemberTranslator(dependencies.SqlExpressionFactory, typeMappingSource),
            new DuckDBDateTimeOffsetMemberTranslator(dependencies.SqlExpressionFactory),
            new DuckDBTimeOnlyMemberTranslator(dependencies.SqlExpressionFactory),
            new DuckDBTimeSpanMemberTranslator(dependencies.SqlExpressionFactory),
            new DuckDBBlobMemberTranslator()
        ]);
    }
}
