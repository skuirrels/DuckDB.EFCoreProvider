using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
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
    private readonly Stack<IReadOnlyList<TableExpressionBase>> _selectTableStack = new();

    public DuckDBQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies, bool reverseNullOrderingEnabled)
        : base(dependencies)
    {
        _reverseNullOrderingEnabled = reverseNullOrderingEnabled;
    }

    /// <summary>
    ///     Overrides <see cref="VisitSelect" /> to push the current select's tables onto a
    ///     stack so that <see cref="VisitColumn" /> can look up entity metadata by table alias
    ///     even when <see cref="ColumnExpression.Column" /> is null (which happens for struct
    ///     sub-fields accessed through navigation/JOIN).
    /// </summary>
    protected override Expression VisitSelect(SelectExpression selectExpression)
    {
        _selectTableStack.Push(selectExpression.Tables);
        try
        {
            return base.VisitSelect(selectExpression);
        }
        finally
        {
            _selectTableStack.Pop();
        }
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
        // Primary path: Column metadata is populated — read the struct field annotation
        // directly from the column's property mappings. Guard against non-direct-table
        // columns (subqueries), which already project flat columns.
        if (columnExpression.Column is { PropertyMappings: { Count: > 0 } propertyMappings }
            && IsDirectTableColumn(columnExpression)
            && GetStructFieldInfo(propertyMappings) is { } structInfo)
        {
            EmitStructFieldAccess(columnExpression, structInfo);
            return columnExpression;
        }

        // Fallback: Column is null — this happens when struct sub-fields are accessed
        // through navigation properties (JOINs). EF Core doesn't populate Column for
        // joined columns, so we traverse the current SelectExpression's tables to find
        // the entity type by alias, then search its properties (including complex type
        // sub-properties) for one whose column name matches and carries our annotation.
        if (columnExpression.Column is null && _selectTableStack.Count > 0)
        {
            foreach (var tables in _selectTableStack)
            {
                if (TryFindStructFieldInfo(tables, columnExpression.TableAlias, columnExpression.Name) is { } fallbackInfo)
                {
                    EmitStructFieldAccess(columnExpression, fallbackInfo);
                    return columnExpression;
                }
            }
        }

        return base.VisitColumn(columnExpression);
    }

    /// <summary>
    ///     Returns <see langword="true" /> when a column originates from a direct table
    ///     reference (e.g. a <c>read_parquet</c> file source) rather than a subquery
    ///     projection. Struct field access syntax should only be emitted for direct-table
    ///     columns; subqueries already project flat columns with no underlying struct.
    /// </summary>
    /// <remarks>
    ///     Looks up the column's table alias in the current <see cref="SelectExpression" />
    ///     FROM clause. If the alias resolves to a <see cref="TableExpression" /> or
    ///     <see cref="FromSqlExpression" />, struct fields are present. If it resolves to a
    ///     <see cref="SelectExpression" /> (subquery), the struct was already flattened.
    ///     When no matching table is found the method conservatively returns
    ///     <see langword="true" /> (outer references and synthesized columns are assumed
    ///     to originate from direct sources).
    /// </remarks>
    private bool IsDirectTableColumn(ColumnExpression columnExpression)
    {
        if (_selectTableStack.Count == 0)
        {
            return true;
        }

        foreach (var tables in _selectTableStack)
        {
            foreach (var table in tables)
            {
                if (!AliasMatches(table, columnExpression.TableAlias))
                {
                    continue;
                }

                // Unwrap joins to get the actual source table.
                TableExpressionBase source = table is JoinExpressionBase join ? join.Table : table;

                // Only TableExpression and FromSqlExpression have struct columns.
                return source is TableExpression || source is FromSqlExpression;
            }
        }

        // No matching alias: outer reference or projection-only column — allow.
        return true;
    }

    private static bool AliasMatches(TableExpressionBase table, string alias)
    {
        if (table is TableExpression te)
        {
            return te.Alias == alias;
        }

        if (table is FromSqlExpression fromSql)
        {
            return fromSql.Alias == alias;
        }

        if (table is SelectExpression se)
        {
            return se.Alias == alias;
        }

        if (table is JoinExpressionBase join)
        {
            return AliasMatches(join.Table, alias);
        }

        return false;
    }

    /// <summary>
    ///     Emits <c>alias."StructColumn".nestedField.leafFieldName</c> for a DuckDB struct
    ///     sub-field access. The struct column name is delimited; intermediate and leaf
    ///     field names are appended unquoted (DuckDB struct field names are case-sensitive
    ///     lowercase identifiers).
    /// </summary>
    private void EmitStructFieldAccess(ColumnExpression columnExpression, DuckDBStructFieldInfo structInfo)
    {
        Sql.Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(columnExpression.TableAlias))
           .Append(".")
           .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(structInfo.StructColumnName));

        foreach (var field in structInfo.NestedFieldNames)
        {
            Sql.Append(".").Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(field));
        }

        // Leaf field name: use the DuckDB-specific name if set, otherwise fall back
        // to the EF column name (e.g. when configured via explicit HasColumnName).
        Sql.Append(".").Append(
            Dependencies.SqlGenerationHelper.DelimitIdentifier(
                structInfo.LeafFieldName ?? columnExpression.Name));
    }

    /// <summary>
    ///     Scans a column's property mappings for a <c>DuckDB:StructField</c>
    ///     annotation and returns the associated <see cref="DuckDBStructFieldInfo" /> if found.
    /// </summary>
    private static DuckDBStructFieldInfo? GetStructFieldInfo(
        IReadOnlyList<IColumnMappingBase> propertyMappings)
    {
        for (var i = 0; i < propertyMappings.Count; i++)
        {
            if (propertyMappings[i].Property.GetStructFieldInfo() is { } info)
            {
                return info;
            }
        }
        return null;
    }

    /// <summary>
    ///     Searches the tables of a SelectExpression for the one matching
    ///     <paramref name="tableAlias" />, then traverses its entity type's properties
    ///     (including complex type sub-properties) for a property whose column name
    ///     matches <paramref name="columnName" /> and carries a <c>DuckDB:StructField</c>
    ///     annotation.
    /// </summary>
    private static DuckDBStructFieldInfo? TryFindStructFieldInfo(
        IReadOnlyList<TableExpressionBase> tables,
        string tableAlias,
        string columnName)
    {
        foreach (var table in tables)
        {
            TableExpression? tableExpression = table switch
            {
                TableExpression te => te,
                JoinExpressionBase jeb when jeb.Table is TableExpression te => te,
                _ => null
            };

            if (tableExpression is null || tableExpression.Alias != tableAlias)
            {
                continue;
            }

            foreach (var mapping in tableExpression.Table.EntityTypeMappings)
            {
                if (mapping.TypeBase is not IEntityType entityType)
                {
                    continue;
                }

                var storeObject = StoreObjectIdentifier.Table(
                    tableExpression.Table.Name,
                    tableExpression.Table.Schema);

                if (FindStructFieldInfoRecursive(entityType, storeObject, columnName) is { } info)
                {
                    return info;
                }
            }

            // Found the table for this alias but no struct field info � stop searching.
            break;
        }

        return null;
    }

    /// <summary>
    ///     Recursively searches an entity type or complex type's properties (and nested
    ///     complex properties) for one whose column name matches <paramref name="columnName" />
    ///     and carries a <c>DuckDB:StructField</c> annotation.
    /// </summary>
    private static DuckDBStructFieldInfo? FindStructFieldInfoRecursive(
        ITypeBase typeBase,
        in StoreObjectIdentifier storeObject,
        string columnName)
    {
        foreach (var property in typeBase.GetProperties())
        {
            if (property.GetColumnName(storeObject) == columnName
                && property.GetStructFieldInfo() is { } info)
            {
                return info;
            }
        }

        foreach (var complexProperty in typeBase.GetComplexProperties())
        {
            if (FindStructFieldInfoRecursive(complexProperty.ComplexType, storeObject, columnName) is { } info)
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
