using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Expressions.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <remarks>
///     <c>ASOF JOIN</c> or <c>ASOF LEFT JOIN</c>. The predicate is a conjunction of equalities followed by exactly
///     one inequality, which DuckDB uses to pick the nearest inner row.
/// </remarks>
public sealed class DuckDBAsOfJoinExpression : PredicateJoinExpressionBase
{
    private static ConstructorInfo? _quotingConstructor;

    public DuckDBAsOfJoinExpression(TableExpressionBase table, SqlExpression joinPredicate, bool isLeft)
        : this(table, joinPredicate, isLeft, annotations: null)
    {
    }

    public DuckDBAsOfJoinExpression(
        TableExpressionBase table,
        SqlExpression joinPredicate,
        bool isLeft,
        IReadOnlyDictionary<string, IAnnotation>? annotations)
        : base(table, joinPredicate, prunable: false, annotations)
        => IsLeft = isLeft;

    /// <summary>Whether outer rows without a match are kept, as <c>ASOF LEFT JOIN</c>.</summary>
    public bool IsLeft { get; }

    public override DuckDBAsOfJoinExpression Update(TableExpressionBase table, SqlExpression joinPredicate)
        => ReferenceEquals(table, Table) && ReferenceEquals(joinPredicate, JoinPredicate)
            ? this
            : new DuckDBAsOfJoinExpression(table, joinPredicate, IsLeft, Annotations);

    public override DuckDBAsOfJoinExpression Update(TableExpressionBase table)
        => ReferenceEquals(table, Table) ? this : new DuckDBAsOfJoinExpression(table, JoinPredicate, IsLeft, Annotations);

    protected override DuckDBAsOfJoinExpression WithAnnotations(IReadOnlyDictionary<string, IAnnotation> annotations)
        => new(Table, JoinPredicate, IsLeft, annotations);

    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DuckDBAsOfJoinExpression).GetConstructor(
                [typeof(TableExpressionBase), typeof(SqlExpression), typeof(bool), typeof(IReadOnlyDictionary<string, IAnnotation>)])!,
            Table.Quote(),
            JoinPredicate.Quote(),
            Constant(IsLeft),
            RelationalExpressionQuotingUtilities.QuoteAnnotations(Annotations));

    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Append(IsLeft ? "ASOF LEFT JOIN " : "ASOF JOIN ");
        expressionPrinter.Visit(Table);
        expressionPrinter.Append(" ON ");
        expressionPrinter.Visit(JoinPredicate);
        PrintAnnotations(expressionPrinter);
    }

    public override bool Equals(object? obj)
        => obj is DuckDBAsOfJoinExpression other && base.Equals(other) && IsLeft == other.IsLeft;

    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), IsLeft);
}