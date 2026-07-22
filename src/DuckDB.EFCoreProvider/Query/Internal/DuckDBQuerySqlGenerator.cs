using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

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

    /// <summary>
    ///     Stack of direct-table alias-to-entity maps for the SELECT expressions currently being generated.
    ///     Only columns whose alias maps to a direct table in the current SELECT are eligible for
    ///     DuckDB struct field access syntax; subquery projections are rendered as ordinary aliases.
    /// </summary>
    private readonly Stack<Dictionary<string, IReadOnlyList<IEntityType>>> _currentSelectTables = new();

    public DuckDBQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies, bool reverseNullOrderingEnabled)
        : base(dependencies)
    {
        _reverseNullOrderingEnabled = reverseNullOrderingEnabled;
    }

    /// <summary>
    ///     Generates SQL for a projection, forcing the <c>AS "alias"</c> clause for
    ///     columns that map to DuckDB STRUCT sub-fields. The base implementation skips
    ///     the alias when <c>column.Name == projection.Alias</c>, but for struct fields
    ///     the emitted SQL (e.g. <c>o."Shipping"."cost"</c>) is named <c>cost</c> by
    ///     DuckDB while the EF column name remains <c>shipping_cost</c> — the names
    ///     don't match the DuckDB output, so omitting the alias causes "column not
    ///     found" errors in outer subquery references.
    /// </summary>
    protected override Expression VisitProjection(ProjectionExpression projectionExpression)
    {
        if (projectionExpression.Expression is ColumnExpression column
            && TryGetDirectStructFieldInfo(column, out var structInfo)
            && structInfo is not null)
        {
            // Render the struct field access SQL (e.g. o."Shipping"."cost").
            EmitStructFieldAccess(column, structInfo);

            // Always emit the alias since the DuckDB output column name (e.g. "cost")
            // differs from the EF projection alias (e.g. "shipping_cost").
            if (projectionExpression.Alias != string.Empty)
            {
                Sql.Append(AliasSeparator)
                    .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(projectionExpression.Alias));
            }

            return projectionExpression;
        }

        return base.VisitProjection(projectionExpression);
    }

    /// <summary>
    ///     Returns <see langword="true" /> when the given column expression both originates
    ///     from a direct table reference in the current <see cref="SelectExpression" /> and
    ///     maps to a DuckDB STRUCT sub-field.
    /// </summary>
    private bool TryGetDirectStructFieldInfo(
        ColumnExpression columnExpression,
        out DuckDBStructFieldInfo? structFieldInfo)
    {
        structFieldInfo = null;

        // Only emit struct access syntax for columns whose table alias belongs to a direct
        // table in the current SELECT. Subqueries already project struct sub-fields as flat
        // columns, so outer references to those projections must render as ordinary aliases.
        if (_currentSelectTables.Count == 0
            || !_currentSelectTables.Peek().TryGetValue(columnExpression.TableAlias, out var entityTypes))
        {
            return false;
        }

        // Prefer the already-resolved column annotation. If the column was synthesized
        // without a model IColumn (e.g. in nested collection subqueries), look up the
        // field info from the entity's struct column map using the table alias.
        var info = GetStructFieldInfo(columnExpression);
        if (info is null && entityTypes is not null)
        {
            foreach (var entityType in entityTypes)
            {
                var columnMap = entityType.GetStructColumnMap();
                if (columnMap?.TryGetValue(columnExpression.Name, out info) == true)
                {
                    break;
                }
            }
        }

        if (info is null)
        {
            return false;
        }

        structFieldInfo = info;
        return true;
    }

    /// <summary>
    ///     Generates SQL for a column, translating DuckDB STRUCT sub-field mappings to
    ///     struct field access syntax. When a column's underlying property carries a
    ///     <c>DuckDB:StructField</c> annotation (set via <c>HasStructField</c>), the SQL
    ///     output becomes <c>t."StructColumn".field.nestedField</c> instead of
    ///     <c>t."ColumnName"</c>. This enables SQL-level projection of individual struct
    ///     sub-fields without encoding the struct path in the column name string.
    /// </summary>
    protected override Expression VisitColumn(ColumnExpression columnExpression)
    {
        if (TryGetDirectStructFieldInfo(columnExpression, out var structInfo)
            && structInfo is not null)
        {
            EmitStructFieldAccess(columnExpression, structInfo);
            return columnExpression;
        }

        return base.VisitColumn(columnExpression);
    }

    /// <inheritdoc />
    protected override Expression VisitSelect(SelectExpression selectExpression)
    {
        var tableMap = new Dictionary<string, IReadOnlyList<IEntityType>>();
        CollectDirectTables(selectExpression.Tables, tableMap);
        _currentSelectTables.Push(tableMap);
        try
        {
            return base.VisitSelect(selectExpression);
        }
        finally
        {
            _currentSelectTables.Pop();
        }
    }

    private static void CollectDirectTables(
        IEnumerable<TableExpressionBase> tables,
        Dictionary<string, IReadOnlyList<IEntityType>> tableMap)
    {
        foreach (var table in tables)
        {
            if (table is JoinExpressionBase join)
            {
                CollectDirectTables(new[] { join.Table }, tableMap);
            }
            else if (table is ITableBasedExpression { Table: not null } tableBased
                     && table.Alias is not null)
            {
                tableMap[table.Alias] = tableBased.Table.EntityTypeMappings
                    .Select(m => m.TypeBase)
                    .OfType<IEntityType>()
                    .ToList();
            }
        }
    }

    /// <summary>
    ///     Emits <c>alias."StructColumn".nestedField.leafFieldName</c> for a DuckDB struct
        ///     sub-field access by delegating to <see cref="VisitStructField" />.
        /// </summary>
        private void EmitStructFieldAccess(ColumnExpression columnExpression, DuckDBStructFieldInfo structInfo)
        {
            // Delegate rendering to the dedicated DuckDBStructFieldExpression so struct access
            // SQL has a single authoring/regeneration point (#3). Constructing the expression
            // here rather than emitting directly lets future phases produce these earlier in
            // the query pipeline (e.g. from the SQL translating visitor) for cleaner flattening.
            Visit(new DuckDBStructFieldExpression(
                columnExpression.TableAlias,
                structInfo.StructColumnName,
                structInfo,
                columnExpression.Type,
                columnExpression.TypeMapping));
        }

        /// <summary>
        ///     Renders a DuckDB struct field access expression:
        ///     <c>alias."StructColumn".nested1.nested2.leaf</c>. The alias and struct column
        ///     name are delimited (per SQL convention); intermediate nested field names and
        ///     the leaf field name are appended unquoted (DuckDB struct field names are
        ///     case-sensitive lowercase identifiers).
        /// </summary>
        /// <remarks>
        ///     This is the single rendering path for <see cref="DuckDBStructFieldExpression" />.
        ///     Resolving STRUCT paths earlier in the query pipeline (per the plan in #3) lets
        ///     subqueries naturally flatten — outer references become plain column projections
        ///     and never reach this renderer.
        /// </remarks>
        protected virtual Expression VisitStructField(DuckDBStructFieldExpression structFieldExpression)
        {
            Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(structFieldExpression.TableAlias))
               .Append(".")
               .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(structFieldExpression.StructColumnName));

            // Intermediate nested field names are appended unquoted: DuckDB struct field names
            // are case-sensitive lowercase identifiers and don't support quoting.
            foreach (var field in structFieldExpression.StructFieldInfo.NestedFieldNames)
            {
                Sql.Append(".").Append(field);
            }

            // Leaf field name: use the DuckDB-specific name if set. The convention and
            // provider always fill this; a null leaf indicates a metadata problem.
            var leafFieldName = structFieldExpression.StructFieldInfo.LeafFieldName
                ?? throw new InvalidOperationException(
                    $"DuckDB struct field metadata for column '{structFieldExpression.StructColumnName}' is missing a leaf field name.");

            Sql.Append(".").Append(leafFieldName);

            return structFieldExpression;
        }

    /// <summary>
    ///     Scans a column for a <c>DuckDB:StructField</c> annotation. Prefer the
    ///     annotation on the <see cref="IColumn" /> because it has already been resolved
    ///     from the entity's column map and is correct for shared complex types used in
    ///     multiple struct columns.
    /// </summary>
    private static DuckDBStructFieldInfo? GetStructFieldInfo(ColumnExpression columnExpression)
    {
        if (columnExpression.Column?.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value
            is DuckDBStructFieldInfo columnInfo)
        {
            return columnInfo;
        }

        if (columnExpression.Column is not { PropertyMappings: { Count: > 0 } propertyMappings })
        {
            return null;
        }

        for (var i = 0; i < propertyMappings.Count; i++)
        {
            if (propertyMappings[i].Property.GetStructFieldInfo() is { } info)
            {
                return info;
            }
        }
        return null;
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
            DuckDBJsonEachExpression e => VisitJsonEach(e),
            DuckDBRowValueExpression e => VisitRowValue(e),
                        DuckDBStructFieldExpression e => VisitStructField(e),
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

    /// <inheritdoc />
    protected override Expression VisitTable(TableExpression tableExpression)
    {
        (string Function, string Path)? fileSource = null;
        foreach (var mapping in tableExpression.Table.EntityTypeMappings)
        {
            if (mapping.TypeBase is IEntityType entityType && GetFileSource(entityType) is { } source)
            {
                fileSource = source;
                break;
            }
        }

        if (fileSource is null)
        {
            return base.VisitTable(tableExpression);
        }

        var (function, path) = fileSource.Value;
        var quotedPath = $"'{path.Replace("'", "''")}'";

        Sql.Append(function)
            .Append("(")
            .Append(quotedPath)
            .Append(")")
            .Append(AliasSeparator)
            .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(tableExpression.Alias));

        return tableExpression;
    }

    private static (string Function, string Path)? GetFileSource(IEntityType entityType)
    {
        var function = entityType.GetDuckDBFileSourceFunction();
        var path = entityType.GetDuckDBFileSourcePath();

        return !string.IsNullOrEmpty(function) && !string.IsNullOrEmpty(path)
            ? (function, path)
            : null;
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
    protected override Expression VisitScalarSubquery(ScalarSubqueryExpression scalarSubqueryExpression)
    {
        if (TryVisitInlineCollectionIndexAccess(scalarSubqueryExpression.Subquery))
        {
            return scalarSubqueryExpression;
        }

        return base.VisitScalarSubquery(scalarSubqueryExpression);
    }

    /// <summary>
    ///     Tries to translate an inline-collection-indexed-by-column pattern into a native DuckDB array access.
    ///     DuckDB does not support correlated columns in LIMIT/OFFSET.
    ///     <para>
    ///         Matches: <c>SELECT v."Value" FROM (VALUES (0, v1), (1, v2), ...) ORDER BY _ord LIMIT 1 OFFSET &lt;expr&gt;</c>
    ///     </para>
    ///     <para>
    ///         Emits: <c>list_value(v1, v2, ...)[&lt;expr&gt; + 1]</c>
    ///     </para>
    /// </summary>
    private bool TryVisitInlineCollectionIndexAccess(SelectExpression selectExpression)
    {
        if (selectExpression.Projection.Count != 1
            || selectExpression.Tables.Count != 1
            || selectExpression.Tables[0] is not ValuesExpression { RowValues: { Count: > 0 } rowValues, ColumnNames.Count: 2 }
            || selectExpression.Limit is not SqlConstantExpression { Value: 1 }
            || selectExpression.Offset is null or SqlConstantExpression
            || selectExpression.Predicate != null
            || selectExpression.GroupBy.Count != 0
            || selectExpression.Having != null
            || selectExpression.IsDistinct)
        {
            return false;
        }

        // Generate: list_value(v1, v2, ...)[offset + 1]
        // DuckDB uses 1-based array indexing; EF Core uses 0-based.
        Sql.Append("list_value(");
        for (var i = 0; i < rowValues.Count; i++)
        {
            if (i > 0)
            {
                Sql.Append(", ");
            }

            // Each row value is (_ord, actual_value); we want the second element.
            Visit(rowValues[i].Values[1]);
        }

        Sql.Append(")[");
        Visit(selectExpression.Offset);
        Sql.Append(" + 1]");

        return true;
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
