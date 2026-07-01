using Microsoft.EntityFrameworkCore.Query;

namespace DuckDB.EFCoreProvider.NTS.Query.Internal;

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