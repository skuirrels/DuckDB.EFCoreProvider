using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Text;

namespace DuckDB.EFCoreProvider.Query.Internal;

public partial class DuckDBQuerySqlGenerator
{
    private void GenerateJsonPath(IReadOnlyList<PathSegment> path)
    {
        var literal = new StringBuilder("$");
        var hasOutput = false;

        for (var i = 0; i < path.Count; i++)
        {
            switch (path[i])
            {
                case { PropertyName: { } propertyName }:
                    literal
                        .Append('.')
                        .Append(QuoteJsonPathElement(propertyName));
                    break;

                case { ArrayIndex: SqlConstantExpression arrayIndex }:
                    literal
                        .Append('[')
                        .Append(FormatJsonArrayIndex(arrayIndex))
                        .Append(']');
                    break;

                case { ArrayIndex: { } arrayIndex }:
                    AppendLiteral();
                    AppendConcatenation();
                    AppendJsonStringLiteral("[");
                    Sql.Append(" || ");
                    Visit(arrayIndex);
                    Sql.Append(" || ");
                    AppendJsonStringLiteral("]");
                    hasOutput = true;
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        AppendLiteral();

        void AppendLiteral()
        {
            if (literal.Length == 0)
            {
                return;
            }

            AppendConcatenation();
            AppendJsonStringLiteral(literal.ToString());
            literal.Clear();
            hasOutput = true;
        }

        void AppendConcatenation()
        {
            if (hasOutput)
            {
                Sql.Append(" || ");
            }
        }
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

        var pendingJsonPath = new StringBuilder();

        for (var i = 0; i < path.Count; i++)
        {
            var pathSegment = path[i];
            var isLast = i == path.Count - 1;

            switch (pathSegment)
            {
                case { PropertyName: { } propertyName }:
                    if (pendingJsonPath.Length > 0)
                    {
                        pendingJsonPath.Append('.').Append(QuoteJsonPathElement(propertyName));
                        continue;
                    }

                    Sql.Append(" ->> ");
                    pendingJsonPath.Append("$.").Append(QuoteJsonPathElement(propertyName));

                    if (isLast || path[i + 1] is { ArrayIndex: not null and not SqlConstantExpression })
                    {
                        AppendJsonStringLiteral(pendingJsonPath.ToString());
                        pendingJsonPath.Clear();
                    }

                    continue;

                case { ArrayIndex: SqlConstantExpression arrayIndex }:
                    if (pendingJsonPath.Length > 0)
                    {
                        pendingJsonPath
                            .Append('[')
                            .Append(FormatJsonArrayIndex(arrayIndex))
                            .Append(']');
                        continue;
                    }

                    Sql.Append(" ->> ");

                    if (isLast || path[i + 1] is { ArrayIndex: not null and not SqlConstantExpression })
                    {
                        Visit(arrayIndex);
                        continue;
                    }

                    pendingJsonPath
                        .Append("$[")
                        .Append(FormatJsonArrayIndex(arrayIndex))
                        .Append(']');
                    continue;

                default:
                    if (pendingJsonPath.Length > 0)
                    {
                        AppendJsonStringLiteral(pendingJsonPath.ToString());
                        pendingJsonPath.Clear();
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

        if (pendingJsonPath.Length > 0)
        {
            AppendJsonStringLiteral(pendingJsonPath.ToString());
        }

        if (needsCast)
        {
            Sql.Append(" AS ");
            Sql.Append(jsonScalarExpression.TypeMapping!.StoreType);
            Sql.Append(")");
        }

        return jsonScalarExpression;
    }

    private void AppendJsonStringLiteral(string value)
        => Sql.Append(DuckDBStringTypeMapping.Default.GenerateSqlLiteral(value));

    private static string FormatJsonArrayIndex(SqlConstantExpression expression)
        => Convert.ToString(expression.Value, CultureInfo.InvariantCulture)
           ?? throw new InvalidOperationException("A JSON array index cannot be null.");

    private static string QuoteJsonPathElement(string element)
        => "\"" + element
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}