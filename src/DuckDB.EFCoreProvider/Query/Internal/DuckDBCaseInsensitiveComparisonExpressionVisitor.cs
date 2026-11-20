using DuckDB.EFCoreProvider.Metadata.Internal;
using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <remarks>
///     Applies <c>UseCaseInsensitiveStringComparisons()</c>: string <c>=</c>, <c>&lt;&gt;</c>, <c>IN</c>,
///     <c>= ANY(...)</c> and <c>array_contains</c> comparisons get <c>COLLATE nocase</c> on one operand, which DuckDB
///     applies to the whole comparison. Comparisons that involve an explicitly collated operand are left unchanged,
///     because DuckDB rejects combining two different collations.
/// </remarks>
public class DuckDBCaseInsensitiveComparisonExpressionVisitor : ExpressionVisitor
{
    // DuckDB compares collation names case-sensitively, and its built-in name is lower case.
    private const string NoCaseCollation = "nocase";

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public DuckDBCaseInsensitiveComparisonExpressionVisitor(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
    }

    /// <inheritdoc />
    protected override Expression VisitExtension(Expression node)
    {
        if (node is ShapedQueryExpression shapedQuery)
        {
            return shapedQuery.Update(Visit(shapedQuery.QueryExpression), Visit(shapedQuery.ShaperExpression));
        }

        var visited = base.VisitExtension(node);

        return visited switch
        {
            SqlBinaryExpression { OperatorType: ExpressionType.Equal or ExpressionType.NotEqual } binary
                when IsCandidate(binary.Left) && IsCandidate(binary.Right) => CollateBinary(binary),

            InExpression { Item: var item } inExpression
                when IsCandidate(item) && !IsNullConstant(item) && InValuesAreCandidates(inExpression)
                => inExpression.Update(Collate(item), inExpression.Subquery, inExpression.Values, inExpression.ValuesParameter),

            DuckDBAnyExpression { Item: var item, Array: var array } any
                when IsCandidate(item) && !IsNullConstant(item) && !IsExplicitlyCollated(array)
                => any.Update(Collate(item), array),

            // The provider translates Contains over a list of strings to array_contains(list, item).
            SqlFunctionExpression
            {
                Name: "array_contains",
                IsBuiltIn: true,
                Instance: null,
                Arguments: [var array, var item]
            } function
                when IsCandidate(item) && !IsNullConstant(item) && !IsExplicitlyCollated(array)
                => function.Update(instance: null, [array, Collate(item)]),

            _ => visited
        };
    }

    private SqlExpression CollateBinary(SqlBinaryExpression binary)
    {
        // DuckDB applies a collation on either operand to the whole comparison, so collate the operand that is safest
        // to collate. A NULL literal cannot take a collation, and a comparison with one is rewritten to IS NULL later,
        // so leave it alone.
        if (IsNullConstant(binary.Left) || IsNullConstant(binary.Right))
        {
            return binary;
        }

        // EF itself compares a column with the same column to correlate group elements, and a foreign key with its
        // principal key for navigations and Include. Collating those would mix groups and entity graphs whose keys
        // differ only by case, while GROUP BY and the change tracker stay case-sensitive.
        if (AreSamePropertyOrKeyPair(binary.Left, binary.Right))
        {
            return binary;
        }

        return Rank(binary.Right) < Rank(binary.Left)
            ? binary.Update(binary.Left, Collate(binary.Right))
            : binary.Update(Collate(binary.Left), binary.Right);

        // Prefer a column or computed value, then a literal, then a parameter, which must first be cast to VARCHAR.
        static int Rank(SqlExpression operand)
            => operand switch
            {
                SqlParameterExpression => 2,
                SqlConstantExpression => 1,
                _ => 0
            };
    }

    private static bool AreSamePropertyOrKeyPair(SqlExpression left, SqlExpression right)
    {
        if (left is not ColumnExpression leftExpression || right is not ColumnExpression rightExpression)
        {
            return false;
        }

        // Grouping key columns carry no table metadata, so a group's correlation is recognised by its column name.
        if (leftExpression.Column is not { } leftColumn || rightExpression.Column is not { } rightColumn)
        {
            return string.Equals(leftExpression.Name, rightExpression.Name, StringComparison.Ordinal);
        }

        foreach (var leftMapping in leftColumn.PropertyMappings)
        {
            foreach (var rightMapping in rightColumn.PropertyMappings)
            {
                if (leftMapping.Property == rightMapping.Property
                    || IsForeignKeyOf(leftMapping.Property, rightMapping.Property)
                    || IsForeignKeyOf(rightMapping.Property, leftMapping.Property))
                {
                    return true;
                }
            }
        }

        return false;

        static bool IsForeignKeyOf(IProperty dependent, IProperty principal)
        {
            foreach (var foreignKey in dependent.GetContainingForeignKeys())
            {
                for (var i = 0; i < foreignKey.Properties.Count; i++)
                {
                    if (foreignKey.Properties[i] == dependent && foreignKey.PrincipalKey.Properties[i] == principal)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    private SqlExpression Collate(SqlExpression operand)
    {
        // A parameter has no type until it is bound, and DuckDB rejects COLLATE on an untyped value.
        var typed = operand is SqlParameterExpression
            ? _sqlExpressionFactory.Convert(operand, typeof(string), operand.TypeMapping)
            : operand;

        return new CollateExpression(typed, NoCaseCollation);
    }

    private static bool InValuesAreCandidates(InExpression inExpression)
    {
        if (inExpression.Values is { } values && values.Any(IsExplicitlyCollated))
        {
            return false;
        }

        return inExpression.Subquery is not { Projection: [var first, ..] } || !IsExplicitlyCollated(first.Expression);
    }

    private static bool IsNullConstant(SqlExpression expression)
        => expression is SqlConstantExpression { Value: null };

    // A plain string operand: string-typed with no value converter, and not already carrying a collation.
    private static bool IsCandidate(SqlExpression expression)
        => expression.Type == typeof(string)
           && expression.TypeMapping is { Converter: null } or null
           && !IsExplicitlyCollated(expression);

    private static bool IsExplicitlyCollated(SqlExpression expression)
        => expression switch
        {
            CollateExpression => true,
            // The runtime model convention keeps each property's collation, which EF itself drops at runtime.
            ColumnExpression { Column: { } column } => column.PropertyMappings.Any(
                mapping => mapping.Property.FindAnnotation(DuckDBAnnotationNames.RuntimeCollation)?.Value is not null),
            _ => false
        };
}