using DuckDB.EFCoreProvider.Query.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Expressions.Internal;

/// <summary>
///     A single GROUP BY element rendering <c>ROLLUP (...)</c>, <c>CUBE (...)</c> or <c>GROUPING SETS (...)</c>.
/// </summary>
/// <remarks>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </remarks>
public sealed class DuckDBGroupingSetsExpression : SqlExpression, IEquatable<DuckDBGroupingSetsExpression>
{
    private static ConstructorInfo? _quotingConstructor;

    /// <summary>Creates a grouping-sets element.</summary>
    /// <param name="kind">The grouping operator.</param>
    /// <param name="terms">The grouping key terms, in key-member order.</param>
    /// <param name="sets">For <see cref="DuckDBGroupingSetsKind.GroupingSets" />, the term positions of each set.</param>
    public DuckDBGroupingSetsExpression(
        DuckDBGroupingSetsKind kind,
        IReadOnlyList<SqlExpression> terms,
        IReadOnlyList<IReadOnlyList<int>> sets)
        : base(typeof(object), typeMapping: null)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(sets);

        Kind = kind;
        Terms = terms.ToArray();

        // The plan validates the sets against the terms and keeps its own copy, so a malformed node cannot be built.
        Sets = new DuckDBGroupingSetsPlan(kind, terms.Count, sets).Sets;
    }

    /// <summary>The grouping operator.</summary>
    public DuckDBGroupingSetsKind Kind { get; }

    /// <summary>The grouping key terms, in key-member order.</summary>
    public IReadOnlyList<SqlExpression> Terms { get; }

    /// <summary>For <see cref="DuckDBGroupingSetsKind.GroupingSets" />, the term positions of each set.</summary>
    public IReadOnlyList<IReadOnlyList<int>> Sets { get; }

    /// <summary>Returns this expression, or a copy using the supplied terms.</summary>
    public DuckDBGroupingSetsExpression Update(IReadOnlyList<SqlExpression> terms)
        => terms.Count == Terms.Count && terms.Zip(Terms).All(pair => ReferenceEquals(pair.First, pair.Second))
            ? this
            : new DuckDBGroupingSetsExpression(Kind, terms, Sets);

    /// <inheritdoc />
    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        SqlExpression[]? visitedTerms = null;
        for (var i = 0; i < Terms.Count; i++)
        {
            var visited = (SqlExpression)visitor.Visit(Terms[i]);
            if (visitedTerms is null && !ReferenceEquals(visited, Terms[i]))
            {
                visitedTerms = new SqlExpression[Terms.Count];
                for (var j = 0; j < i; j++)
                {
                    visitedTerms[j] = Terms[j];
                }
            }

            if (visitedTerms is not null)
            {
                visitedTerms[i] = visited;
            }
        }

        return visitedTerms is null ? this : new DuckDBGroupingSetsExpression(Kind, visitedTerms, Sets);
    }

    /// <inheritdoc />
    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DuckDBGroupingSetsExpression).GetConstructor(
                [typeof(DuckDBGroupingSetsKind), typeof(IReadOnlyList<SqlExpression>), typeof(IReadOnlyList<IReadOnlyList<int>>)])!,
            Constant(Kind),
            NewArrayInit(typeof(SqlExpression), Terms.Select(term => term.Quote())),
            NewArrayInit(
                typeof(IReadOnlyList<int>),
                Sets.Select(set => (Expression)NewArrayInit(typeof(int), set.Select(index => Constant(index))))));

    /// <inheritdoc />
    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        switch (Kind)
        {
            case DuckDBGroupingSetsKind.Rollup:
                expressionPrinter.Append("ROLLUP ");
                PrintList(expressionPrinter, Terms);
                break;
            case DuckDBGroupingSetsKind.Cube:
                expressionPrinter.Append("CUBE ");
                PrintList(expressionPrinter, Terms);
                break;
            default:
                expressionPrinter.Append("GROUPING SETS (");
                for (var i = 0; i < Sets.Count; i++)
                {
                    if (i > 0)
                    {
                        expressionPrinter.Append(", ");
                    }

                    PrintList(expressionPrinter, Sets[i].Select(index => Terms[index]).ToArray());
                }

                expressionPrinter.Append(")");
                break;
        }
    }

    private static void PrintList(ExpressionPrinter expressionPrinter, IReadOnlyList<SqlExpression> terms)
    {
        expressionPrinter.Append("(");
        for (var i = 0; i < terms.Count; i++)
        {
            if (i > 0)
            {
                expressionPrinter.Append(", ");
            }

            expressionPrinter.Visit(terms[i]);
        }

        expressionPrinter.Append(")");
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is DuckDBGroupingSetsExpression other && Equals(other);

    /// <inheritdoc />
    public bool Equals(DuckDBGroupingSetsExpression? other)
        => other is not null
            && (ReferenceEquals(this, other)
                || (Kind == other.Kind
                    && Terms.SequenceEqual(other.Terms)
                    && Sets.Count == other.Sets.Count
                    && Sets.Zip(other.Sets).All(pair => pair.First.SequenceEqual(pair.Second))));

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        foreach (var term in Terms)
        {
            hash.Add(term);
        }

        foreach (var set in Sets)
        {
            hash.Add(set.Count);
        }

        return hash.ToHashCode();
    }
}
