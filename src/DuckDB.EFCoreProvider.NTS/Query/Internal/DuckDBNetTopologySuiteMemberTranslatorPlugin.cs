using Microsoft.EntityFrameworkCore.Query;

namespace DuckDB.EFCoreProvider.NTS.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBNetTopologySuiteMemberTranslatorPlugin : IMemberTranslatorPlugin
{
    public DuckDBNetTopologySuiteMemberTranslatorPlugin(ISqlExpressionFactory sqlExpressionFactory)
    {
        Translators =
        [
            new DuckDBGeometryMemberTranslator(sqlExpressionFactory),
            new DuckDBGeometryCollectionMemberTranslator(sqlExpressionFactory),
            new DuckDBLineStringMemberTranslator(sqlExpressionFactory),
            new DuckDBMultiLineStringMemberTranslator(sqlExpressionFactory),
            new DuckDBPointMemberTranslator(sqlExpressionFactory),
            new DuckDBPolygonMemberTranslator(sqlExpressionFactory)
        ];
    }

    public IEnumerable<IMemberTranslator> Translators { get; }
}