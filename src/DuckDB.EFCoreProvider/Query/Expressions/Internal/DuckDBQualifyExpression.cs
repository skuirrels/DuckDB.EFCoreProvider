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
///     A condition that belongs in the SELECT's <c>QUALIFY</c> clause rather than its <c>WHERE</c> clause. Translation
///     puts it in the SELECT's predicate as a top-level conjunct, and SQL generation moves it to <c>QUALIFY</c>.
/// </remarks>
public sealed class DuckDBQualifyExpression : SqlExpression, IEquatable<DuckDBQualifyExpression>
{
    private static ConstructorInfo? _quotingConstructor;

    public DuckDBQualifyExpression(SqlExpression predicate, RelationalTypeMapping? typeMapping)
        : base(typeof(bool), typeMapping)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        Predicate = predicate;
    }

    public SqlExpression Predicate { get; }

    public DuckDBQualifyExpression Update(SqlExpression predicate)
        => ReferenceEquals(predicate, Predicate) ? this : new DuckDBQualifyExpression(predicate, TypeMapping);

    protected override Expression VisitChildren(ExpressionVisitor visitor)
        => Update((SqlExpression)visitor.Visit(Predicate));

    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DuckDBQualifyExpression).GetConstructor([typeof(SqlExpression), typeof(RelationalTypeMapping)])!,
            Predicate.Quote(),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));

    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Append("QUALIFY(");
        expressionPrinter.Visit(Predicate);
        expressionPrinter.Append(")");
    }

    public override bool Equals(object? obj)
        => obj is DuckDBQualifyExpression other && Equals(other);

    public bool Equals(DuckDBQualifyExpression? other)
        => other is not null
            && (ReferenceEquals(this, other) || (base.Equals(other) && Predicate.Equals(other.Predicate)));

    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), Predicate);
}