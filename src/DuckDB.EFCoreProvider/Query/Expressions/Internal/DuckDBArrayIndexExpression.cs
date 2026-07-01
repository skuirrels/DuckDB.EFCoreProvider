using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Expressions.Internal;

/// <summary>
///     An SQL expression that represents an indexing into a DuckDB array.
/// </summary>
/// <remarks>
///     <see cref="SqlBinaryExpression" /> specifically disallows having an <see cref="SqlBinaryExpression.OperatorType" />
///     of value <see cref="ExpressionType.ArrayIndex" /> as arrays are a DuckDB-only feature.
/// </remarks>
public class DuckDBArrayIndexExpression : SqlExpression, IEquatable<DuckDBArrayIndexExpression>
{
    private static ConstructorInfo? _quotingConstructor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DuckDBArrayIndexExpression" /> class.
    /// </summary>
    /// <param name="array">The array to index into.</param>
    /// <param name="index">A position in the array to index into.</param>
    /// <param name="nullable">Whether the expression is nullable.</param>
    /// <param name="type">The <see cref="Type" /> of the expression.</param>
    /// <param name="typeMapping">The <see cref="RelationalTypeMapping" /> associated with the expression.</param>
    /// <exception cref="ArgumentNullException"><paramref name="array" /> or <paramref name="index" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">The array is not an array type, its element type does not match <paramref name="type" />, or the index is not an <see cref="int" />.</exception>
    public DuckDBArrayIndexExpression(
        SqlExpression array,
        SqlExpression index,
        bool nullable,
        Type type,
        RelationalTypeMapping? typeMapping)
        : base(type.UnwrapNullableType(), typeMapping)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentNullException.ThrowIfNull(index);
        
        if (!array.Type.TryGetElementType(out var elementType))
        {
            throw new ArgumentException("Array expression must be of an array type", nameof(array));
        }

        if (type.UnwrapNullableType() != elementType.UnwrapNullableType())
        {
            throw new ArgumentException($"Mismatch between array type ({array.Type.Name}) and expression type ({type})");
        }

        if (index.Type != typeof(int))
        {
            throw new ArgumentException("Index expression must be of type int", nameof(index));
        }

        Array = array;
        Index = index;
        IsNullable = nullable;
    }

    /// <summary>
    ///     The array being indexed.
    /// </summary>
    public virtual SqlExpression Array { get; }

    /// <summary>
    ///     The index in the array.
    /// </summary>
    public virtual SqlExpression Index { get; }

    /// <summary>
    ///     Whether the expression is nullable.
    /// </summary>
    public virtual bool IsNullable { get; set; }

    /// <summary>
    ///     Creates a new expression that is like this one, but using the supplied children. If they are the
    ///     same, it will return this expression.
    /// </summary>
    /// <param name="array">The array to index into.</param>
    /// <param name="index">A position in the array to index into.</param>
    public virtual DuckDBArrayIndexExpression Update(SqlExpression array, SqlExpression index)
    {
        return array == Array && index == Index
            ? this
            : new DuckDBArrayIndexExpression(array, index, IsNullable, Type, TypeMapping);
    }

    /// <inheritdoc />
    public override Expression Quote()
    {
        return New(
            _quotingConstructor ??= typeof(DuckDBArrayIndexExpression).GetConstructor(
                [typeof(SqlExpression), typeof(SqlExpression), typeof(bool), typeof(Type), typeof(RelationalTypeMapping)])!,
            Array.Quote(),
            Index.Quote(),
            Constant(IsNullable),
            Constant(Type),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));
    }

    /// <inheritdoc />
    protected override Expression VisitChildren(ExpressionVisitor visitor)
        => Update((SqlExpression)visitor.Visit(Array), (SqlExpression)visitor.Visit(Index));

    /// <inheritdoc />
    public virtual bool Equals(DuckDBArrayIndexExpression? other)
        => ReferenceEquals(this, other)
           || other is not null
           && base.Equals(other)
           && Array.Equals(other.Array)
           && Index.Equals(other.Index)
           && IsNullable == other.IsNullable;

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is DuckDBArrayIndexExpression e && Equals(e);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), Array, Index, IsNullable);

    /// <inheritdoc />
    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Visit(Array);
        expressionPrinter.Append("[");
        expressionPrinter.Visit(Index);
        expressionPrinter.Append("]");
    }

    /// <inheritdoc />
    public override string ToString() => $"{Array}[{Index}]";
}
