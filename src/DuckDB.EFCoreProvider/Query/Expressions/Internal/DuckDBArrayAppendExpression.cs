using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Expressions.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public sealed class DuckDBArrayAppendExpression : Expression
{
    public DuckDBArrayAppendExpression(Expression source, Expression value, Type expressionType)
    {
        Source = source;
        Value = value;
        Type = expressionType;
    }

    /// <summary>
    ///     Gets the source collection expression.
    /// </summary>
    public Expression Source { get; }

    /// <summary>
    ///     Gets the value expression to append to the source collection.
    /// </summary>
    public Expression Value { get; }

    /// <inheritdoc />
    public override ExpressionType NodeType => ExpressionType.Extension;

    /// <inheritdoc />
    public override Type Type { get; }

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var source = visitor.Visit(Source);
        var value = visitor.Visit(Value);

        return source != Source || value != Value
            ? new DuckDBArrayAppendExpression(source, value, Type)
            : this;
    }
}
