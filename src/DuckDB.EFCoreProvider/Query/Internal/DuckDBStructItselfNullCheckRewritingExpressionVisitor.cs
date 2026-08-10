using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     Replaces EF-generated whole-complex null-presence markers with struct-itself checks.
/// </summary>
/// <remarks>
///     Runs after struct-field rewriting so every struct-mapped leaf inside a
///     <see cref="DuckDBStructPresenceCheckExpression" /> has been resolved to a
///     <see cref="DuckDBStructFieldExpression" />. The marker's recorded depth selects the
///     physical struct member whose presence is checked (zero selects the struct root itself)
///     and the resolved leaf's <see cref="DuckDBStructFieldExpression.FieldPath" /> supplies the
///     path prefix to that member. A single
///     <c>struct_col IS NULL</c> / <c>struct_col."path" IS NULL</c> comparison replaces the
///     per-leaf comparison EF generated, which both avoids referencing missing keys on sparse
///     STRUCTs and minimizes the number of null checks.
/// </remarks>
internal sealed class DuckDBStructItselfNullCheckRewritingExpressionVisitor : ExpressionVisitor
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public DuckDBStructItselfNullCheckRewritingExpressionVisitor(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
    }

    /// <inheritdoc />
    [return: NotNullIfNotNull(nameof(node))]
    public override Expression? Visit(Expression? node)
        => node switch
        {
            DuckDBStructPresenceCheckExpression presenceCheck => RewritePresenceCheck(presenceCheck),
            ShapedQueryExpression shapedQueryExpression => shapedQueryExpression.Update(
                Visit(shapedQueryExpression.QueryExpression),
                Visit(shapedQueryExpression.ShaperExpression)),
            _ => base.Visit(node)
        };

    private Expression RewritePresenceCheck(DuckDBStructPresenceCheckExpression presenceCheck)
    {
        if (!TryFindResolvedStructField(presenceCheck.CheckedExpression, out var structField))
        {
            // No struct-mapped leaf was resolved (for example the leaf lives in a subquery
            // projection); restore the narrowed comparison unchanged.
            return presenceCheck.CheckedExpression;
        }

        var source = structField!.Source;
        var depth = presenceCheck.Depth;
        var fieldPath = depth == 0 ? [] : structField.FieldPath.Take(depth).ToArray();
        SqlExpression target = fieldPath.Length == 0
            ? source
            : new DuckDBStructFieldExpression(source, fieldPath, typeof(object));

        return presenceCheck.OperatorType == ExpressionType.Equal
            ? _sqlExpressionFactory.IsNull(target)
            : _sqlExpressionFactory.IsNotNull(target);
    }

    private static bool TryFindResolvedStructField(SqlExpression expression, out DuckDBStructFieldExpression? structField)
    {
        structField = null;
        var visitor = new ResolvedStructFieldFindingVisitor();
        visitor.Visit(expression);
        structField = visitor.StructField;
        return structField is not null;
    }

    private sealed class ResolvedStructFieldFindingVisitor : ExpressionVisitor
    {
        public DuckDBStructFieldExpression? StructField { get; private set; }

        public override Expression? Visit(Expression? node)
        {
            if (StructField is null && node is DuckDBStructFieldExpression structFieldExpression)
            {
                StructField = structFieldExpression;
            }

            return StructField is null
                ? base.Visit(node)
                : node;
        }
    }
}
