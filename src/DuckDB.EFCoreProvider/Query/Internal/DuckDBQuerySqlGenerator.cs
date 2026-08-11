using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Diagnostics;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public partial class DuckDBQuerySqlGenerator : QuerySqlGenerator
{
    private readonly bool _reverseNullOrderingEnabled;

    public DuckDBQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies, bool reverseNullOrderingEnabled)
        : base(dependencies)
    {
        _reverseNullOrderingEnabled = reverseNullOrderingEnabled;
    }

    /// <summary>
    ///     Renders a DuckDB struct field access expression by visiting its already-resolved
    ///     <see cref="DuckDBStructFieldExpression.Source" /> and appending the immutable
    ///     physical field path: <c>{rendered source}."nested1"."nested2"."leaf"</c>.
    /// </summary>
    /// <remarks>
    ///     The source expression is the authoritative root; the renderer never reconstructs it
    ///     from alias/root compatibility strings and never inspects EF metadata. Every physical
    ///     path segment is delimited through <see cref="ISqlGenerationHelper" /> so explicitly
    ///     configured names containing spaces, quotes, or reserved words remain valid. Resolving
    ///     STRUCT paths earlier in the query pipeline lets subqueries naturally flatten — outer
    ///     references become plain column projections and never reach this renderer.
    /// </remarks>
    protected virtual Expression VisitStructField(DuckDBStructFieldExpression structFieldExpression)
    {
        // The source is normally a physical root column (rendered as alias."StructColumn"),
        // but it is always the visitable, already-resolved root rather than a renderer inference.
        Visit(structFieldExpression.Source);

        var helper = Dependencies.SqlGenerationHelper;
        foreach (var field in structFieldExpression.FieldPath)
        {
            Sql.Append(".").Append(helper.DelimitIdentifier(field));
        }

        return structFieldExpression;
    }

    /// <summary>
    ///     Renders a whole-struct projection by visiting its source column and emitting no
    ///     field-extraction suffix: <c>{rendered source}</c>. Field extraction happens client-side.
    /// </summary>
    protected virtual Expression VisitWholeStruct(DuckDBWholeStructExpression wholeStructExpression)
    {
        if (wholeStructExpression.SuppressSource)
        {
            // Keep EF's complex-materializer null sentinel non-null without repeating the STRUCT payload.
            Sql.Append("''");
        }
        else
        {
            Visit(wholeStructExpression.Source);
        }

        return wholeStructExpression;
    }

    /// <inheritdoc />
    protected override void GenerateLimitOffset(SelectExpression selectExpression)
    {
        if (selectExpression.Limit is not null)
        {
            Sql.AppendLine().Append("LIMIT ");
            Visit(selectExpression.Limit);
        }

        if (selectExpression.Offset is not null)
        {
            if (selectExpression.Limit is null)
            {
                Sql.AppendLine();
            }
            else
            {
                Sql.Append(" ");
            }

            Sql.Append("OFFSET ");
            Visit(selectExpression.Offset);
        }
    }

    /// <inheritdoc />
    protected override string GetOperator(SqlBinaryExpression binaryExpression)
    {
        return binaryExpression.OperatorType switch
        {
            ExpressionType.Add when binaryExpression.Type == typeof(string) => " || ",
            ExpressionType.LeftShift => " << ",
            ExpressionType.RightShift => " >> ",
            _ => base.GetOperator(binaryExpression)
        };
    }

    /// <inheritdoc />
    protected override Expression VisitOrdering(OrderingExpression ordering)
    {
        var result = base.VisitOrdering(ordering);

        if (_reverseNullOrderingEnabled)
        {
            Sql.Append(ordering.IsAscending ? " NULLS FIRST" : " NULLS LAST");
        }

        return result;
    }

    /// <inheritdoc />
    protected override Expression VisitExtension(Expression extensionExpression)
    {
        return extensionExpression switch
        {
            DuckDBAnyExpression e => VisitArrayAny(e),
            DuckDBBinaryExpression e => VisitBinary(e),
            DuckDBArrayIndexExpression e => VisitArrayIndex(e),
            DuckDBArraySliceExpression e => VisitArraySlice(e),
            DuckDBNewArrayExpression e => VisitNewArray(e),
            DuckDBJsonEachExpression e => VisitJsonEach(e),
            DuckDBRowValueExpression e => VisitRowValue(e),
            DuckDBStructFieldExpression e => VisitStructField(e),
            DuckDBWholeStructExpression e => VisitWholeStruct(e),
            _ => base.VisitExtension(extensionExpression)
        };
    }

    /// <inheritdoc />
    protected override Expression VisitSqlBinary(SqlBinaryExpression sqlBinaryExpression)
    {
        return sqlBinaryExpression.OperatorType switch
        {
            ExpressionType.ArrayIndex => VisitArrayIndex(sqlBinaryExpression),
            _ => base.VisitSqlBinary(sqlBinaryExpression)
        };
    }

    /// <inheritdoc />
    protected override Expression VisitTableValuedFunction(TableValuedFunctionExpression tableValuedFunctionExpression)
    {
        if (tableValuedFunctionExpression is DuckDBUnnestExpression unnestExpression)
        {
            return VisitUnnest(unnestExpression);
        }

        return base.VisitTableValuedFunction(tableValuedFunctionExpression);
    }

    /// <summary>
    ///     Generates SQL for a DuckDB <c>unnest</c> expression.
    ///     <para>
    ///         Without ordinality: <c>unnest(array) AS "alias"("colname")</c>
    ///     </para>
    ///     <para>
    ///         With ordinality: <c>(SELECT unnest(array) AS "colname", generate_subscripts(array, 1) AS "ordinality") AS "alias"</c>
    ///     </para>
    /// </summary>
    protected virtual Expression VisitUnnest(DuckDBUnnestExpression expression)
    {
        if (expression.WithOrdinality)
        {
            // DuckDB does not support WITH ORDINALITY; use generate_subscripts instead.
            // Wrap in a subquery so that the ordinality column can be referenced by the outer query.
            Sql.Append("(SELECT unnest(");
            Visit(expression.Array);
            Sql.Append(")");

            if (expression.ColumnInfos is { Count: > 0 } colInfosOrd)
            {
                Sql.Append(" AS ")
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(colInfosOrd[0].Name));
            }

            Sql.Append(", generate_subscripts(");
            Visit(expression.Array);
            Sql.Append(", 1) AS ")
                .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier("ordinality"))
                .Append(")");
        }
        else
        {
            Sql.Append("unnest(");
            Visit(expression.Array);
            Sql.Append(")");
        }

        Sql.Append(AliasSeparator)
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(expression.Alias));

        if (!expression.WithOrdinality && expression.ColumnInfos is { Count: > 0 } colInfos)
        {
            Sql.Append("(");
            for (var i = 0; i < colInfos.Count; i++)
            {
                if (i > 0)
                {
                    Sql.Append(", ");
                }

                Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(colInfos[i].Name));
            }

            Sql.Append(")");
        }

        return expression;
    }

    /// <inheritdoc />
    protected override Expression VisitCrossApply(CrossApplyExpression crossApplyExpression)
    {
        Sql.Append("CROSS JOIN LATERAL ");
        Visit(crossApplyExpression.Table);

        return crossApplyExpression;
    }

    /// <inheritdoc />
    protected override Expression VisitOuterApply(OuterApplyExpression outerApplyExpression)
    {
        Sql.Append("LEFT JOIN LATERAL ");
        Visit(outerApplyExpression.Table);
        Sql.Append(" ON true");

        return outerApplyExpression;
    }

    protected virtual Expression VisitArrayAny(DuckDBAnyExpression expression)
    {
        Visit(expression.Item);

        Sql.Append(" = ANY(");
        Visit(expression.Array);
        Sql.Append(")");
        return expression;
    }

    protected virtual Expression VisitArrayIndex(SqlBinaryExpression sqlBinaryExpression)
    {
        Visit(sqlBinaryExpression.Left);
        Sql.Append("[");
        Visit(sqlBinaryExpression.Right);
        Sql.Append("]");
        return sqlBinaryExpression;
    }

    protected virtual Expression VisitArrayIndex(DuckDBArrayIndexExpression expression)
    {
        var requiresParentheses = RequiresParentheses(expression, expression.Array);

        if (requiresParentheses)
        {
            Sql.Append("(");
        }

        Visit(expression.Array);

        if (requiresParentheses)
        {
            Sql.Append(")");
        }

        Sql.Append("[");
        Visit(expression.Index);
        Sql.Append("]");
        return expression;
    }

    protected virtual Expression VisitNewArray(DuckDBNewArrayExpression expression)
    {
        Sql.Append("list_value(");

        for (var i = 0; i < expression.Expressions.Count; i++)
        {
            if (i > 0)
            {
                Sql.Append(", ");
            }

            Visit(expression.Expressions[i]);
        }

        Sql.Append(")");
        return expression;
    }

    protected virtual Expression VisitArraySlice(DuckDBArraySliceExpression expression)
    {
        var requiresParentheses = RequiresParentheses(expression, expression.Array);

        if (requiresParentheses)
        {
            Sql.Append("(");
        }

        Visit(expression.Array);

        if (requiresParentheses)
        {
            Sql.Append(")");
        }

        Sql.Append("[");
        Visit(expression.LowerBound);
        Sql.Append(":");
        Visit(expression.UpperBound);
        Sql.Append("]");
        return expression;
    }

    protected virtual Expression VisitBinary(DuckDBBinaryExpression binaryExpression)
    {
        switch (binaryExpression.OperatorType)
        {
            case ExpressionType.LeftShift:
                Sql.Append("CASE WHEN (");
                Visit(binaryExpression.Left);
                Sql.Append(" >= 0) THEN ");
                Visit(binaryExpression.Left);
                Sql.Append(" << ");
                Visit(binaryExpression.Right);
                Sql.Append(" ELSE NULL END");
                break;

            case ExpressionType.RightShift:
                Sql.Append("CASE WHEN (");
                Visit(binaryExpression.Left);
                Sql.Append(" >= 0) THEN ");
                Visit(binaryExpression.Left);
                Sql.Append(" >> ");
                Visit(binaryExpression.Right);
                Sql.Append(" ELSE NULL END");
                break;

            default:
                throw new UnreachableException("Unknown binary operator");
        }

        return binaryExpression;
    }

    protected virtual Expression VisitRowValue(DuckDBRowValueExpression rowValueExpression)
    {
        Sql.Append("(");

        var values = rowValueExpression.Values;
        var count = values.Count;
        for (var i = 0; i < count; i++)
        {
            Visit(values[i]);

            if (i < count - 1)
            {
                Sql.Append(", ");
            }
        }

        Sql.Append(")");

        return rowValueExpression;
    }

    protected virtual Expression VisitJsonEach(DuckDBJsonEachExpression expression)
    {
        Sql.Append("json_each(");

        Visit(expression.JsonExpression);

        var path = expression.Path;

        if (path is not null)
        {
            Sql.Append(", ");

            GenerateJsonPath(path);
        }

        Sql.Append(")");

        Sql.Append(AliasSeparator).Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(expression.Alias));

        return expression;
    }

    /// <inheritdoc />
    protected override void GenerateValues(ValuesExpression valuesExpression)
    {
        if (valuesExpression.RowValues is null)
        {
            throw new UnreachableException();
        }

        if (valuesExpression.RowValues.Count == 0)
        {
            throw new InvalidOperationException(RelationalStrings.EmptyCollectionNotSupportedAsInlineQueryRoot);
        }

        var rowValues = valuesExpression.RowValues;

        Sql.Append("VALUES ");

        for (var i = 0; i < rowValues.Count; i++)
        {
            if (i > 0)
            {
                Sql.Append(", ");
            }

            Visit(valuesExpression.RowValues[i]);
        }
    }

    /// <inheritdoc />
    protected override Expression VisitValues(ValuesExpression valuesExpression)
    {
        base.VisitValues(valuesExpression);

        Sql.Append("(");

        for (var i = 0; i < valuesExpression.ColumnNames.Count; i++)
        {
            if (i > 0)
            {
                Sql.Append(", ");
            }

            Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(valuesExpression.ColumnNames[i]));
        }

        Sql.Append(")");

        return valuesExpression;
    }
}
