using Microsoft.EntityFrameworkCore.Query;

namespace DuckDB.EFCoreProvider.NTS.Query.Internal;

public class DuckDBNetTopologySuiteAggregateMethodCallTranslatorPlugin : IAggregateMethodCallTranslatorPlugin
{
    public DuckDBNetTopologySuiteAggregateMethodCallTranslatorPlugin(ISqlExpressionFactory sqlExpressionFactory)
    {
        Translators = [new DuckDBNetTopologySuiteAggregateMethodTranslator(sqlExpressionFactory)];
    }

    public IEnumerable<IAggregateMethodCallTranslator> Translators { get; }
}