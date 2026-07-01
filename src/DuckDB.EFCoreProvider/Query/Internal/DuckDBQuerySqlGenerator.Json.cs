using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Diagnostics;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

public partial class DuckDBQuerySqlGenerator
{
    
    private void GenerateJsonPath(IReadOnlyList<PathSegment> path)
    {
        Sql.Append("'$");

        for (var i = 0; i < path.Count; i++)
        {
            switch (path[i])
            {
                case { PropertyName: { } propertyName }:
                    Sql.Append(".").Append(propertyName);
                    break;

                case { ArrayIndex: { } arrayIndex }:
                    Sql.Append("[");

                    if (arrayIndex is SqlConstantExpression)
                    {
                        Visit(arrayIndex);
                    }
                    else
                    {
                        Sql.Append("' || ");
                        Visit(arrayIndex);
                        Sql.Append(" || '");
                    }

                    Sql.Append("]");
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        Sql.Append("'");
    }

    protected override Expression VisitJsonScalar(JsonScalarExpression jsonScalarExpression)
    {
        var path = jsonScalarExpression.Path;
        if (path.Count == 0)
        {
            Visit(jsonScalarExpression.Json);
            return jsonScalarExpression;
        }

        var needsCast = jsonScalarExpression.TypeMapping is not (null or StringTypeMapping or DuckDBJsonTypeMapping);

        if (needsCast)
        {
            Sql.Append("CAST(");
        }

        Visit(jsonScalarExpression.Json);

        var inJsonpathString = false;

        for (var i = 0; i < path.Count; i++)
        {
            var pathSegment = path[i];
            var isLast = i == path.Count - 1;

            switch (pathSegment)
            {
                case { PropertyName: { } propertyName }:
                    if (inJsonpathString)
                    {
                        Sql.Append(".").Append(Dependencies.SqlGenerationHelper.DelimitJsonPathElement(propertyName));
                        continue;
                    }

                    Sql.Append(" ->> ");

                    if (isLast || path[i + 1] is { ArrayIndex: not null and not SqlConstantExpression })
                    {
                        Sql.Append("'").Append(Dependencies.SqlGenerationHelper.DelimitJsonPathElement(propertyName)).Append("'");
                        continue;
                    }

                    Sql.Append("'$.").Append(Dependencies.SqlGenerationHelper.DelimitJsonPathElement(propertyName));
                    inJsonpathString = true;
                    continue;

                case { ArrayIndex: SqlConstantExpression arrayIndex }:
                    if (inJsonpathString)
                    {
                        Sql.Append("[");
                        Visit(pathSegment.ArrayIndex);
                        Sql.Append("]");
                        continue;
                    }

                    Sql.Append(" ->> ");

                    if (isLast || path[i + 1] is { ArrayIndex: not null and not SqlConstantExpression })
                    {
                        Visit(arrayIndex);
                        continue;
                    }

                    Sql.Append("'$[");
                    Visit(arrayIndex);
                    Sql.Append("]");
                    inJsonpathString = true;
                    continue;

                default:
                    if (inJsonpathString)
                    {
                        Sql.Append("'");
                        inJsonpathString = false;
                    }

                    Sql.Append(" ->> ");

                    Debug.Assert(pathSegment.ArrayIndex is not null);

                    var requiresParentheses = RequiresParentheses(jsonScalarExpression, pathSegment.ArrayIndex);
                    if (requiresParentheses)
                    {
                        Sql.Append("(");
                    }

                    Visit(pathSegment.ArrayIndex);

                    if (requiresParentheses)
                    {
                        Sql.Append(")");
                    }

                    continue;
            }
        }

        if (inJsonpathString)
        {
            Sql.Append("'");
        }

        if (needsCast)
        {
            Sql.Append(" AS ");
            Sql.Append(jsonScalarExpression.TypeMapping!.StoreType);
            Sql.Append(")");
        }

        return jsonScalarExpression;
    }
}
