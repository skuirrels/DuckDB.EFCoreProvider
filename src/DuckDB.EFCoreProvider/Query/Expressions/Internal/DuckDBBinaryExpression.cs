using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Expressions.Internal;

/// <summary>
///     An expression that represents a DuckDB-specific binary operation in a SQL tree.
/// </summary>
public class DuckDBBinaryExpression : SqlExpression
{
    private static ConstructorInfo? _quotingConstructor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DuckDBBinaryExpression"/> class.
    /// </summary>
    /// <param name="operatorType">The operator to apply.</param>
    /// <param name="left">An expression which is left operand.</param>
    /// <param name="right">An expression which is right operand.</param>
    /// <param name="type">The <see cref="Type" /> of the expression.</param>
    /// <param name="typeMapping">The <see cref="RelationalTypeMapping" /> associated with the expression.</param>
    /// <exception cref="InvalidOperationException"><paramref name="operatorType" /> is not a supported binary operator.</exception>
    public DuckDBBinaryExpression(
        ExpressionType operatorType,
        SqlExpression left,
        SqlExpression right,
        Type type,
        RelationalTypeMapping? typeMapping = null)
        : base(type, typeMapping)
    {
        if (!IsValidOperator(operatorType))
        {
            throw new InvalidOperationException("Invalid operator type for binary expression: " + operatorType);
        }

        OperatorType = operatorType;
        Left = left;
        Right = right;
    }

    /// <summary>
    ///     The operator of this DuckDB binary operation.
    /// </summary>
    public virtual ExpressionType OperatorType { get; }

    /// <summary>
    ///     The left operand.
    /// </summary>
    public virtual SqlExpression Left { get; set; }

    /// <summary>
    ///     The right operand.
    /// </summary>
    public virtual SqlExpression Right { get; set; }

    /// <inheritdoc />
    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var left = (SqlExpression)visitor.Visit(Left);
        var right = (SqlExpression)visitor.Visit(Right);
        
        return Update(left, right);
    }

    /// <summary>
    ///     Creates a new expression that is like this one, but using the supplied children. If all of the children are the same, it will
    ///     return this expression.
    /// </summary>
    /// <param name="left">The <see cref="Left" /> property of the result.</param>
    /// <param name="right">The <see cref="Right" /> property of the result.</param>
    /// <returns>This expression if no children changed, or an expression with the updated children.</returns>
    public virtual DuckDBBinaryExpression Update(SqlExpression left, SqlExpression right)
    {
        return left != Left || right != Right
            ? new DuckDBBinaryExpression(OperatorType, left, Right, Type, TypeMapping)
            : this;
    }

    internal static bool IsValidOperator(ExpressionType operatorType)
    {
        switch (operatorType)
        {
            case ExpressionType.LeftShift:
            case ExpressionType.RightShift:
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc />
    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DuckDBBinaryExpression).GetConstructor(
                [typeof(ExpressionType), typeof(SqlExpression), typeof(SqlExpression), typeof(Type), typeof(RelationalTypeMapping)])!,
            Constant(OperatorType),
            Left.Quote(),
            Right.Quote(),
            Constant(Type),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));

    /// <inheritdoc />
    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        var requiresBrackets = RequiresBrackets(Left);

        if (requiresBrackets)
        {
            expressionPrinter.Append("(");
        }

        expressionPrinter.Visit(Left);

        if (requiresBrackets)
        {
            expressionPrinter.Append(")");
        }

        expressionPrinter.Append(expressionPrinter.GenerateBinaryOperator(OperatorType));

        requiresBrackets = RequiresBrackets(Right);

        if (requiresBrackets)
        {
            expressionPrinter.Append("(");
        }

        expressionPrinter.Visit(Right);

        if (requiresBrackets)
        {
            expressionPrinter.Append(")");
        }

        static bool RequiresBrackets(SqlExpression expression)
            => expression is DuckDBBinaryExpression or LikeExpression;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj != null
           && (ReferenceEquals(this, obj)
               || obj is DuckDBBinaryExpression sqlBinaryExpression
               && Equals(sqlBinaryExpression));

    private bool Equals(DuckDBBinaryExpression sqlBinaryExpression)
        => base.Equals(sqlBinaryExpression)
           && OperatorType == sqlBinaryExpression.OperatorType
           && Left.Equals(sqlBinaryExpression.Left)
           && Right.Equals(sqlBinaryExpression.Right);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), OperatorType, Left, Right);
}
