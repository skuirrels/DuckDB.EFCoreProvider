using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DuckDB.EFCoreProvider.Query.Expressions.Internal;

/// <summary>
///     An expression that represents a DuckDB-specific row value expression in a SQL tree.
/// </summary>
public class DuckDBRowValueExpression : SqlExpression, IEquatable<DuckDBRowValueExpression>
{
    private static ConstructorInfo? _quotingConstructor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DuckDBRowValueExpression" /> class.
    /// </summary>
    /// <param name="values">The values of this DuckDB row value expression.</param>
    /// <param name="type">The <see cref="Type" /> of the expression (a tuple type).</param>
    /// <param name="typeMapping">The <see cref="RelationalTypeMapping" /> associated with the expression.</param>
    /// <exception cref="ArgumentNullException"><paramref name="values" /> is <see langword="null" />.</exception>
    public DuckDBRowValueExpression(
        IReadOnlyList<SqlExpression> values,
        Type type,
        RelationalTypeMapping? typeMapping = null)
        : base(type, typeMapping)
    {
        ArgumentNullException.ThrowIfNull(values);
        Debug.Assert(type.IsAssignableTo(typeof(ITuple)), $"Type '{type}' isn't an ITuple");

        Values = values;
    }

    /// <summary>
    ///     The values of this DuckDB row value expression.
    /// </summary>
    public virtual IReadOnlyList<SqlExpression> Values { get; }

    /// <inheritdoc />
    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        SqlExpression[]? newRowValues = null;

        for (var i = 0; i < Values.Count; i++)
        {
            var rowValue = Values[i];
            var visited = (SqlExpression)visitor.Visit(rowValue);
            if (visited != rowValue && newRowValues is null)
            {
                newRowValues = new SqlExpression[Values.Count];
                for (var j = 0; j < i; j++)
                {
                    newRowValues[j] = Values[j];
                }
            }

            if (newRowValues is not null)
            {
                newRowValues[i] = visited;
            }
        }

        return newRowValues is null ? this : new DuckDBRowValueExpression(newRowValues, Type);
    }

    /// <summary>
    ///     Creates a new expression that is like this one, but using the supplied children. If all of the children are the same, it will
    ///     return this expression.
    /// </summary>
    public virtual DuckDBRowValueExpression Update(IReadOnlyList<SqlExpression> values)
        => values.Count == Values.Count && values.Zip(Values, (x, y) => (x, y)).All(tup => tup.x == tup.y)
            ? this
            : new DuckDBRowValueExpression(values, Type);

    /// <inheritdoc />
    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DuckDBRowValueExpression).GetConstructor(
                [typeof(IReadOnlyList<SqlExpression>), typeof(Type), typeof(RelationalTypeMapping)])!,
            NewArrayInit(typeof(SqlExpression), initializers: Values.Select(a => a.Quote())),
            Constant(Type),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));

    /// <inheritdoc />
    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Append("(");

        var count = Values.Count;
        for (var i = 0; i < count; i++)
        {
            expressionPrinter.Visit(Values[i]);

            if (i < count - 1)
            {
                expressionPrinter.Append(", ");
            }
        }

        expressionPrinter.Append(")");
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is DuckDBRowValueExpression other && Equals(other);

    /// <inheritdoc />
    public virtual bool Equals(DuckDBRowValueExpression? other)
    {
        if (other is null || !base.Equals(other) || other.Values.Count != Values.Count)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        for (var i = 0; i < Values.Count; i++)
        {
            if (!other.Values[i].Equals(Values[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hashCode = new HashCode();

        foreach (var rowValue in Values)
        {
            hashCode.Add(rowValue);
        }

        return hashCode.ToHashCode();
    }
}
