using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Expressions.Internal;

/// <summary>
///     Semantic provenance for an EF-generated complex-type null-presence check
///     (<c>entity.Complex == null</c> / <c>entity.Complex != null</c>).
/// </summary>
/// <remarks>
///     When EF Core translates a whole-complex null comparison it narrows the check to one or
///     more representative leaf columns (for example the first non-nullable member). The wrapped
///     <see cref="CheckedExpression" /> is exactly that narrowed SQL comparison. Postprocessors
///     use the marker as provenance so that only EF-generated presence checks - never user leaf-null
///     filters such as <c>entity.Complex.Leaf == null</c> - are rebuilt to reference actually
///     projected STRUCT fields. The marker is SQL-transparent: rendering simply emits
///     <see cref="CheckedExpression" />.
/// </remarks>
public sealed class DuckDBStructPresenceCheckExpression : SqlExpression, IEquatable<DuckDBStructPresenceCheckExpression>
{
    private static ConstructorInfo? _quotingConstructor;

    public DuckDBStructPresenceCheckExpression(
        ExpressionType operatorType,
        SqlExpression checkedExpression)
        : this(operatorType, checkedExpression, typeof(bool), checkedExpression.TypeMapping)
    {
    }

    private DuckDBStructPresenceCheckExpression(
        ExpressionType operatorType,
        SqlExpression checkedExpression,
        Type type,
        RelationalTypeMapping? typeMapping)
        : base(type, typeMapping)
    {
        ArgumentNullException.ThrowIfNull(checkedExpression);
        if (operatorType is not (ExpressionType.Equal or ExpressionType.NotEqual))
        {
            throw new ArgumentException(
                "A struct presence check must be an Equal or NotEqual comparison.",
                nameof(operatorType));
        }

        OperatorType = operatorType;
        CheckedExpression = checkedExpression;
    }

    /// <summary>
    ///     The polarity of the presence check. Equal means "the complex property is null" and
    ///     NotEqual means "the complex property is not null".
    /// </summary>
    public ExpressionType OperatorType { get; }

    /// <summary>The narrowed SQL comparison EF Core generated for the whole-complex null check.</summary>
    public SqlExpression CheckedExpression { get; }

    public DuckDBStructPresenceCheckExpression Update(SqlExpression checkedExpression)
        => ReferenceEquals(checkedExpression, CheckedExpression)
            ? this
            : new DuckDBStructPresenceCheckExpression(OperatorType, checkedExpression, Type, TypeMapping);

    protected override Expression VisitChildren(ExpressionVisitor visitor)
        => Update((SqlExpression)visitor.Visit(CheckedExpression)!);

    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DuckDBStructPresenceCheckExpression).GetConstructor(
                [
                    typeof(ExpressionType),
                    typeof(SqlExpression),
                    typeof(Type),
                    typeof(RelationalTypeMapping)
                ])!,
            Constant(OperatorType),
            CheckedExpression.Quote(),
            Constant(Type),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));

    protected override void Print(ExpressionPrinter expressionPrinter)
        => expressionPrinter.Visit(CheckedExpression);

    public override string ToString()
        => $"StructPresenceCheck[{OperatorType}] {CheckedExpression}";

    public bool Equals(DuckDBStructPresenceCheckExpression? other)
        => other is not null
            && base.Equals(other)
            && OperatorType == other.OperatorType
            && CheckedExpression.Equals(other.CheckedExpression);

    public override bool Equals(object? obj)
        => Equals(obj as DuckDBStructPresenceCheckExpression);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(base.GetHashCode());
        hash.Add(OperatorType);
        hash.Add(CheckedExpression);
        return hash.ToHashCode();
    }
}
