using Microsoft.EntityFrameworkCore.Query;

namespace DuckDB.EFCoreProvider.NTS.Query.Internal;

public class DuckDBNetTopologySuiteMethodCallTranslatorPlugin : IMethodCallTranslatorPlugin
{
    public DuckDBNetTopologySuiteMethodCallTranslatorPlugin(ISqlExpressionFactory sqlExpressionFactory)
    {
        Translators =
        [
            new DuckDBGeometryMethodTranslator(sqlExpressionFactory),
            new DuckDBGeometryCollectionMethodTranslator(sqlExpressionFactory),
            new DuckDBLineStringMethodTranslator(sqlExpressionFactory),
            new DuckDBPolygonMethodTranslator(sqlExpressionFactory)
        ];
    }

    public IEnumerable<IMethodCallTranslator> Translators { get; }
}