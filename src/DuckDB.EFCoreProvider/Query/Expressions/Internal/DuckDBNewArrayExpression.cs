using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Expressions.Internal;

/// <summary>
///     Represents creating a new DuckDB array.
/// </summary>
public class DuckDBNewArrayExpression : SqlExpression
{
    private static ConstructorInfo? _quotingConstructor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DuckDBNewArrayExpression" /> class.
    /// </summary>
    /// <param name="expressions">The values to initialize the elements of the new array.</param>
    /// <param name="type">The <see cref="Type" /> of the expression.</param>
    /// <param name="typeMapping">The <see cref="RelationalTypeMapping" /> associated with the expression.</param>
    public DuckDBNewArrayExpression(IReadOnlyList<SqlExpression> expressions, Type type, RelationalTypeMapping? typeMapping)
        : base(type, typeMapping)
    {
        ArgumentNullException.ThrowIfNull(expressions);
        
        if (type.TryGetElementType(typeof(IEnumerable<>)) is null)
        {
            throw new ArgumentException($"{nameof(DuckDBNewArrayExpression)} must have an IEnumerable<T> type");
        }

        Expressions = expressions;
    }

    /// <summary>
    ///     The values to initialize the elements of the new array.
    /// </summary>
    public virtual IReadOnlyList<SqlExpression> Expressions { get; }

    /// <inheritdoc />
    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        List<SqlExpression>? newExpressions = null;

        for (var i = 0; i < Expressions.Count; i++)
        {
            var expression = Expressions[i];
            var visitedExpression = (SqlExpression)visitor.Visit(expression);

            if (visitedExpression != expression && newExpressions is null)
            {
                newExpressions = [];

                for (var j = 0; j < i; j++)
                {
                    newExpressions.Add(Expressions[j]);
                }
            }

            newExpressions?.Add(visitedExpression);
        }

        return newExpressions is null
            ? this
            : new DuckDBNewArrayExpression(newExpressions, Type, TypeMapping);
    }

    /// <summary>
    ///     Creates a new expression that is like this one, but using the supplied children. If all of the children are the same, it will
    ///     return this expression.
    /// </summary>
    /// <param name="expressions">The values to initialize the elements of the new array.</param>
    /// <returns>This expression if no children changed, or an expression with the updated children.</returns>
    public virtual DuckDBNewArrayExpression Update(IReadOnlyList<SqlExpression> expressions)
    {
        ArgumentNullException.ThrowIfNull(expressions);

        return expressions == Expressions
            ? this
            : new DuckDBNewArrayExpression(expressions, Type, TypeMapping);
    }

    /// <inheritdoc />
    public override Expression Quote()
    {
        return New(
            _quotingConstructor ??= typeof(DuckDBNewArrayExpression).GetConstructor(
                [typeof(IReadOnlyList<SqlExpression>), typeof(Type), typeof(RelationalTypeMapping)])!,
            NewArrayInit(typeof(SqlExpression), initializers: Expressions.Select(a => a.Quote())),
            Constant(Type),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));
    }

    /// <inheritdoc />
    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        ArgumentNullException.ThrowIfNull(expressionPrinter);

        expressionPrinter.Append("[");

        var first = true;

        foreach (SqlExpression expression in Expressions)
        {
            if (!first)
            {
                expressionPrinter.Append(", ");
            }

            first = false;
            expressionPrinter.Visit(expression);
        }

        expressionPrinter.Append("]");

        if (TypeMapping != null)
        {
            expressionPrinter.Append("::").Append(TypeMapping.StoreType).Append("[]");
        }
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is not null
           && (ReferenceEquals(this, obj)
               || obj is DuckDBNewArrayExpression sqlBinaryExpression
               && Equals(sqlBinaryExpression));

    private bool Equals(DuckDBNewArrayExpression newArrayExpression)
        => base.Equals(newArrayExpression)
           && Expressions.SequenceEqual(newArrayExpression.Expressions);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(base.GetHashCode());

        for (var i = 0; i < Expressions.Count; i++)
        {
            hash.Add(i);
        }

        return hash.ToHashCode();
    }
}
