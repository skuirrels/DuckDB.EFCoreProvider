using DuckDB.EFCoreProvider.Query.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
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
///     A ranking window function, <c>row_number()</c>, <c>rank()</c> or <c>dense_rank()</c>, over
///     <c>PARTITION BY ... ORDER BY ...</c>. It never returns NULL.
/// </remarks>
public sealed class DuckDBRankingWindowExpression : SqlExpression, IEquatable<DuckDBRankingWindowExpression>
{
    private static ConstructorInfo? _quotingConstructor;

    public DuckDBRankingWindowExpression(
        DuckDBRankingFunction function,
        IReadOnlyList<SqlExpression> partitions,
        IReadOnlyList<OrderingExpression> orderings,
        RelationalTypeMapping? typeMapping)
        : base(typeof(long), typeMapping)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        ArgumentNullException.ThrowIfNull(orderings);
        if (orderings.Count == 0)
        {
            throw new ArgumentException("A ranking window function needs at least one ordering.", nameof(orderings));
        }

        Function = function;
        Partitions = partitions.ToArray();
        Orderings = orderings.ToArray();
    }

    public DuckDBRankingFunction Function { get; }

    public IReadOnlyList<SqlExpression> Partitions { get; }

    public IReadOnlyList<OrderingExpression> Orderings { get; }

    internal string FunctionName
        => Function switch
        {
            DuckDBRankingFunction.RowNumber => "row_number",
            DuckDBRankingFunction.Rank => "rank",
            _ => "dense_rank"
        };

    public DuckDBRankingWindowExpression Update(
        IReadOnlyList<SqlExpression> partitions,
        IReadOnlyList<OrderingExpression> orderings)
        => partitions.SequenceEqual(Partitions, ReferenceEqualityComparer.Instance)
            && orderings.SequenceEqual(Orderings, ReferenceEqualityComparer.Instance)
                ? this
                : new DuckDBRankingWindowExpression(Function, partitions, orderings, TypeMapping);

    protected override Expression VisitChildren(ExpressionVisitor visitor)
        => Update(
            Partitions.Select(partition => (SqlExpression)visitor.Visit(partition)).ToArray(),
            Orderings.Select(ordering => (OrderingExpression)visitor.Visit(ordering)).ToArray());

    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DuckDBRankingWindowExpression).GetConstructor(
                [typeof(DuckDBRankingFunction), typeof(IReadOnlyList<SqlExpression>), typeof(IReadOnlyList<OrderingExpression>), typeof(RelationalTypeMapping)])!,
            Constant(Function),
            NewArrayInit(typeof(SqlExpression), Partitions.Select(partition => partition.Quote())),
            NewArrayInit(typeof(OrderingExpression), Orderings.Select(ordering => ordering.Quote())),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));

    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Append(FunctionName).Append("() OVER (");
        if (Partitions.Count > 0)
        {
            expressionPrinter.Append("PARTITION BY ");
            expressionPrinter.VisitCollection(Partitions);
            expressionPrinter.Append(" ");
        }

        expressionPrinter.Append("ORDER BY ");
        expressionPrinter.VisitCollection(Orderings);
        expressionPrinter.Append(")");
    }

    public override bool Equals(object? obj)
        => obj is DuckDBRankingWindowExpression other && Equals(other);

    public bool Equals(DuckDBRankingWindowExpression? other)
        => other is not null
            && (ReferenceEquals(this, other)
                || (base.Equals(other)
                    && Function == other.Function
                    && Partitions.SequenceEqual(other.Partitions)
                    && Orderings.SequenceEqual(other.Orderings)));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(base.GetHashCode());
        hash.Add(Function);
        foreach (var partition in Partitions)
        {
            hash.Add(partition);
        }

        foreach (var ordering in Orderings)
        {
            hash.Add(ordering);
        }

        return hash.ToHashCode();
    }
}