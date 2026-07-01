using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBSqlTranslatingExpressionVisitor : RelationalSqlTranslatingExpressionVisitor
{
    private static readonly Dictionary<string, string> TimeUnits = new()
    {
        [nameof(TimeSpan.TotalDays)] = "day",
        [nameof(TimeSpan.TotalHours)] = "hour",
        [nameof(TimeSpan.TotalMinutes)] = "minute",
        [nameof(TimeSpan.TotalSeconds)] = "second",
        [nameof(TimeSpan.TotalMilliseconds)] = "millisecond",
        [nameof(TimeSpan.TotalMicroseconds)] = "microsecond",
        [nameof(TimeSpan.TotalNanoseconds)] = "nanosecond"
    };

    private static readonly MethodInfo StringJoinWithStringArray =
        typeof(string).GetMethod(nameof(string.Join), [typeof(string), typeof(string[])])!;

    private static readonly MethodInfo StringJoinWithObjectArray =
        typeof(string).GetMethod(nameof(string.Join), [typeof(string), typeof(object[])])!;

    private static readonly MethodInfo StringJoinWithCharStringArray =
        typeof(string).GetMethod(nameof(string.Join), [typeof(char), typeof(string[])])!;

    private static readonly MethodInfo StringJoinWithCharObjectArray =
        typeof(string).GetMethod(nameof(string.Join), [typeof(char), typeof(object[])])!;

    public DuckDBSqlTranslatingExpressionVisitor(RelationalSqlTranslatingExpressionVisitorDependencies dependencies, QueryCompilationContext queryCompilationContext, QueryableMethodTranslatingExpressionVisitor queryableMethodTranslatingExpressionVisitor) : base(dependencies, queryCompilationContext, queryableMethodTranslatingExpressionVisitor)
    {
    }

    /// <inheritdoc />
    public override SqlExpression? GenerateGreatest(IReadOnlyList<SqlExpression> expressions, Type resultType)
    {
        var resultTypeMapping = ExpressionExtensions.InferTypeMapping(expressions);

        return Dependencies.SqlExpressionFactory.Function("greatest", expressions, nullable: true, Enumerable.Repeat(true, expressions.Count), resultType, resultTypeMapping);
    }

    /// <inheritdoc />
    public override SqlExpression? GenerateLeast(IReadOnlyList<SqlExpression> expressions, Type resultType)
    {
        var resultTypeMapping = ExpressionExtensions.InferTypeMapping(expressions);

        return Dependencies.SqlExpressionFactory.Function("least", expressions, nullable: true, Enumerable.Repeat(true, expressions.Count), resultType, resultTypeMapping);
    }

    /// <inheritdoc />
    protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
    {
        var method = methodCallExpression.Method;

        if (method.DeclaringType == typeof(string)
            && (method == StringJoinWithStringArray
                || method == StringJoinWithObjectArray
                || method == StringJoinWithCharStringArray
                || method == StringJoinWithCharObjectArray)
            && methodCallExpression.Arguments[1] is NewArrayExpression newArrayExpression)
        {
            if (TranslationFailed(methodCallExpression.Arguments[0], Visit(methodCallExpression.Arguments[0]), out var separator))
            {
                return QueryCompilationContext.NotTranslatedExpression;
            }

            var elements = newArrayExpression.Expressions;
            var rewrittenArgs = new SqlExpression[elements.Count + 1];
            rewrittenArgs[0] = separator!;

            for (var i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                if (TranslationFailed(element, Visit(element), out var sqlElement))
                {
                    return QueryCompilationContext.NotTranslatedExpression;
                }

                rewrittenArgs[i + 1] = sqlElement switch
                {
                    SqlConstantExpression { Value: null } => Dependencies.SqlExpressionFactory.Constant(string.Empty, typeof(string)),
                    ColumnExpression { IsNullable: false } => sqlElement,
                    _ => Dependencies.SqlExpressionFactory.Coalesce(sqlElement!, Dependencies.SqlExpressionFactory.Constant(string.Empty, typeof(string)))
                };
            }

            var argumentsPropagateNullability = new bool[rewrittenArgs.Length];
            argumentsPropagateNullability[0] = true;

            return Dependencies.SqlExpressionFactory.Function(
                "concat_ws",
                rewrittenArgs,
                nullable: true,
                argumentsPropagateNullability,
                typeof(string));
        }

        return base.VisitMethodCall(methodCallExpression);
    }

    /// <inheritdoc />
    protected override Expression VisitMember(MemberExpression memberExpression)
    {
        if (memberExpression.Expression is BinaryExpression { NodeType: ExpressionType.Subtract } binaryExpression)
        {
            var sqlExpressionFactory = (DuckDBSqlExpressionFactory)Dependencies.SqlExpressionFactory;

            if ((binaryExpression.Left.Type == typeof(DateTime) && binaryExpression.Right.Type == typeof(DateTime)) ||
                (binaryExpression.Left.Type == typeof(DateTimeOffset) && binaryExpression.Right.Type == typeof(DateTimeOffset)))
            {
                if (TimeUnits.TryGetValue(memberExpression.Member.Name, out var unit))
                {
                    return sqlExpressionFactory.DateDiff(
                        unit,
                        Translate(binaryExpression.Left)!,
                        Translate(binaryExpression.Right)!);
                }
            }
        }

        return base.VisitMember(memberExpression);
    }

    /// <inheritdoc />
    protected override Expression VisitUnary(UnaryExpression unaryExpression)
    {
        switch (unaryExpression.NodeType)
        {
            case ExpressionType.ArrayLength:
                if (TranslationFailed(unaryExpression.Operand, Visit(unaryExpression.Operand), out var sqlOperand))
                {
                    return QueryCompilationContext.NotTranslatedExpression;
                }

                if (sqlOperand!.Type == typeof(byte[]) && sqlOperand.TypeMapping is DuckDBBlobTypeMapping or null)
                {
                    return this.Dependencies.SqlExpressionFactory.Function(
                        "octet_length",
                        [sqlOperand],
                        nullable: true,
                        argumentsPropagateNullability: [true],
                        typeof(int));
                }

                break;

            case ExpressionType.Convert
                when unaryExpression.Type == typeof(ITuple) && unaryExpression.Operand.Type.IsAssignableTo(typeof(ITuple)):
                return Visit(unaryExpression.Operand);
        }

        return base.VisitUnary(unaryExpression);
    }

    /// <inheritdoc />
    protected override Expression VisitBinary(BinaryExpression binaryExpression)
    {
        switch (binaryExpression.NodeType)
        {
            case ExpressionType.LeftShift:
            case ExpressionType.RightShift:
                var left = Translate(binaryExpression.Left)!;
                var right = Translate(binaryExpression.Right)!;
                return new DuckDBBinaryExpression(
                    binaryExpression.NodeType,
                    left,
                    right,
                    binaryExpression.Type,
                    ExpressionExtensions.InferTypeMapping(left, right)!);
            case ExpressionType.ExclusiveOr:
                var leftXor = Translate(binaryExpression.Left)!;
                var rightXor = Translate(binaryExpression.Right)!;

                if (leftXor.Type == typeof(bool) && rightXor.Type == typeof(bool))
                {
                    return Dependencies.SqlExpressionFactory.OrElse(
                        Dependencies.SqlExpressionFactory.AndAlso(
                            leftXor,
                            Dependencies.SqlExpressionFactory.Not(rightXor)),
                        Dependencies.SqlExpressionFactory.AndAlso(
                            Dependencies.SqlExpressionFactory.Not(leftXor),
                            rightXor)
                    );
                }

                return Dependencies.SqlExpressionFactory.Function(
                    name: "xor",
                    arguments: [leftXor, rightXor],
                    nullable: true,
                    argumentsPropagateNullability: [true, true],
                    returnType: binaryExpression.Type,
                    typeMapping: ExpressionExtensions.InferTypeMapping(leftXor, rightXor)!);
            default:
                return base.VisitBinary(binaryExpression);
        }
    }

    /// <inheritdoc />
    protected override Expression VisitNew(NewExpression newExpression)
    {
        var visitedNewExpression = base.VisitNew(newExpression);

        if (visitedNewExpression != QueryCompilationContext.NotTranslatedExpression)
        {
            return visitedNewExpression;
        }

        if (newExpression.Type.IsAssignableTo(typeof(ITuple)))
        {
            return TryTranslateArguments(out var sqlArguments)
                ? new DuckDBRowValueExpression(sqlArguments, newExpression.Type)
                : QueryCompilationContext.NotTranslatedExpression;
        }

        return visitedNewExpression;

        bool TryTranslateArguments(out SqlExpression[] sqlArguments)
        {
            sqlArguments = new SqlExpression[newExpression.Arguments.Count];
            for (var i = 0; i < sqlArguments.Length; i++)
            {
                var argument = newExpression.Arguments[i];
                if (TranslationFailed(argument, Visit(argument), out var sqlArgument))
                {
                    return false;
                }

                sqlArguments[i] = sqlArgument!;
            }

            return true;
        }
    }

    [DebuggerStepThrough]
    private static bool TranslationFailed(Expression? original, Expression? translation, out SqlExpression? castTranslation)
    {
        if (original is not null && translation is not SqlExpression)
        {
            castTranslation = null;
            return true;
        }

        castTranslation = translation as SqlExpression;
        return false;
    }
}
