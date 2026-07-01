using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Expressions.Internal;

/// <summary>
///     Represents a DuckDB ANY expression.
/// </summary>
public class DuckDBAnyExpression : SqlExpression, IEquatable<DuckDBAnyExpression>
{
    private static ConstructorInfo? _quotingConstructor;

    /// <inheritdoc />
    public override Type Type
        => typeof(bool);

    /// <summary>
    ///     The value to test against the <see cref="Array" />.
    /// </summary>
    public virtual SqlExpression Item { get; }

    /// <summary>
    ///     The array of values or patterns to test for the <see cref="Item" />.
    /// </summary>
    public virtual SqlExpression Array { get; }

    /// <summary>
    ///     Initializes a new instance of the <see cref="DuckDBAnyExpression" /> class.
    /// </summary>
    /// <param name="item">The value to find.</param>
    /// <param name="array">The array to search.</param>
    /// <param name="typeMapping">The type mapping for the expression.</param>
    /// <exception cref="ArgumentException"><paramref name="array" /> is not an enumerable type.</exception>
    public DuckDBAnyExpression(
        SqlExpression item,
        SqlExpression array,
        RelationalTypeMapping? typeMapping)
        : base(typeof(bool), typeMapping)
    {
        if (array is not SqlConstantExpression { Value: null })
        {
            if (array.Type.TryGetElementType(typeof(IEnumerable<>)) is null)
            {
                throw new ArgumentException("Array expression must be an IEnumerable", nameof(array));
            }
        }

        Item = item;
        Array = array;
    }

    /// <summary>
    ///     Creates a new expression that is like this one, but using the supplied children. If all of the children are the same, it will
    ///     return this expression.
    /// </summary>
    /// <param name="item">The <see cref="Item" /> property of the result.</param>
    /// <param name="array">The <see cref="Array" /> property of the result.</param>
    /// <returns>This expression if no children changed, or an expression with the updated children.</returns>
    public virtual DuckDBAnyExpression Update(SqlExpression item, SqlExpression array)
        => item != Item || array != Array
            ? new DuckDBAnyExpression(item, array, TypeMapping)
            : this;

    /// <inheritdoc />
    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DuckDBAnyExpression).GetConstructor(
                [typeof(SqlExpression), typeof(SqlExpression), typeof(RelationalTypeMapping)])!,
            Item.Quote(),
            Array.Quote(),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));

    /// <inheritdoc />
    protected override Expression VisitChildren(ExpressionVisitor visitor)
        => Update((SqlExpression)visitor.Visit(Item), (SqlExpression)visitor.Visit(Array));

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is DuckDBAnyExpression e && Equals(e);

    /// <inheritdoc />
    public virtual bool Equals(DuckDBAnyExpression? other)
        => ReferenceEquals(this, other)
            || other is not null
            && base.Equals(other)
            && Item.Equals(other.Item)
            && Array.Equals(other.Array);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), Item, Array);

    /// <inheritdoc />
    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Visit(Item);
        expressionPrinter.Append(" = ANY(");
        expressionPrinter.Visit(Array);
        expressionPrinter.Append(")");
    }

    /// <inheritdoc />
    public override string ToString()
        => $"{Item} = ANY({Array})";
}
