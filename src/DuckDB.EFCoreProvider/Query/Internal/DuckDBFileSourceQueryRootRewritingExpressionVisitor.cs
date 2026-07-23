using DuckDB.EFCoreProvider.Metadata.Internal;
using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

internal sealed class DuckDBFileSourceQueryRootRewritingExpressionVisitor(ISqlExpressionFactory sqlExpressionFactory)
    : ExpressionVisitor
{
    protected override Expression VisitExtension(Expression node)
    {
        if (node is ShapedQueryExpression shapedQuery)
        {
            return shapedQuery.Update(Visit(shapedQuery.QueryExpression), shapedQuery.ShaperExpression);
        }

        if (node is not TableExpression tableExpression)
        {
            return base.VisitExtension(node);
        }

        if (!DuckDBFileSourceDefinition.TryCreate(tableExpression.Table, out var fileSource))
        {
            return base.VisitExtension(node);
        }

        var path = sqlExpressionFactory.ApplyDefaultTypeMapping(sqlExpressionFactory.Constant(fileSource.Path));
        return new DuckDBFileSourceExpression(tableExpression.Alias, fileSource.Function, path);
    }
}