using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Expressions.Internal;

/// <summary>
///     A guard placed on the correlated element query of a ROLLUP, CUBE or GROUPING SETS group.
/// </summary>
/// <remarks>
///     <para>
///         EF Core reads group elements by correlating on key equality, which cannot match the NULL key members of a
///         subtotal row. The guard is never rendered: query postprocessing rejects any query that still contains it,
///         so element access fails at compilation instead of returning incomplete groups.
///     </para>
///     <para>
///         This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///         the same compatibility standards as public APIs. It may be changed or removed without notice in
///         any release. You should only use it directly in your code with extreme caution and knowing that
///         doing so can result in application failures when updating to a new Entity Framework Core release.
///     </para>
/// </remarks>
public sealed class DuckDBGroupingSetsElementAccessExpression : SqlExpression
{
    private static ConstructorInfo? _quotingConstructor;

    /// <summary>Creates the guard.</summary>
    public DuckDBGroupingSetsElementAccessExpression(RelationalTypeMapping? typeMapping)
        : base(typeof(bool), typeMapping)
    {
    }

    /// <inheritdoc />
    protected override Expression VisitChildren(ExpressionVisitor visitor)
        => this;

    /// <inheritdoc />
    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DuckDBGroupingSetsElementAccessExpression).GetConstructor([typeof(RelationalTypeMapping)])!,
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));

    /// <inheritdoc />
    protected override void Print(ExpressionPrinter expressionPrinter)
        => expressionPrinter.Append("GROUPING_SETS_ELEMENT_ACCESS");

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is DuckDBGroupingSetsElementAccessExpression;

    /// <inheritdoc />
    public override int GetHashCode()
        => typeof(DuckDBGroupingSetsElementAccessExpression).GetHashCode();
}
