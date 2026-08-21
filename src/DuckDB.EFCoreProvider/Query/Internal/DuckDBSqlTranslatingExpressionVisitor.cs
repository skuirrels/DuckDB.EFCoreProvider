using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
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

    private readonly IModel _model;

    public DuckDBSqlTranslatingExpressionVisitor(RelationalSqlTranslatingExpressionVisitorDependencies dependencies, QueryCompilationContext queryCompilationContext, QueryableMethodTranslatingExpressionVisitor queryableMethodTranslatingExpressionVisitor) : base(dependencies, queryCompilationContext, queryableMethodTranslatingExpressionVisitor)
    {
        _model = queryCompilationContext.Model;
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
            case ExpressionType.Equal or ExpressionType.NotEqual
                when IsComplexTypeNullComparison(binaryExpression):
                // EF narrows a whole-complex null comparison to representative leaf columns
                // (see StructuralEquality.TryGenerateComparisons). Wrap the narrowed result in a
                // provenance marker carrying the nesting depth of the checked complex below its
                // struct root so a postprocessor can replace it with a single struct-itself
                // IS NULL / IS NOT NULL check. That avoids per-field checks that binder-error on
                // sparse STRUCTs and minimizes the number of null checks.
                var visited = base.VisitBinary(binaryExpression);
                return visited is SqlExpression visitedSql
                    ? CreateStructPresenceCheck(binaryExpression, visitedSql) ?? visited
                    : visited;
            default:
                return base.VisitBinary(binaryExpression);
        }
    }

    /// <summary>
    ///     Detects a whole-complex null comparison (<c>entity.Complex == null</c> / <c>!= null</c>),
    ///     where one side is a null constant and the other side's type is a model complex type.
    /// </summary>
    private bool IsComplexTypeNullComparison(BinaryExpression binaryExpression)
    {
        var left = RemoveImplicitConvert(binaryExpression.Left);
        var right = RemoveImplicitConvert(binaryExpression.Right);

        Expression nonNullOperand;
        if (left is ConstantExpression { Value: null })
        {
            nonNullOperand = right;
        }
        else if (right is ConstantExpression { Value: null })
        {
            nonNullOperand = left;
        }
        else
        {
            return false;
        }

        var clrType = Nullable.GetUnderlyingType(nonNullOperand.Type) ?? nonNullOperand.Type;
        return EnumerateComplexTypes(_model)
            .Any(complexType => complexType.ClrType == clrType);
    }

    /// <summary>
    ///     Builds a struct presence marker for a whole-complex null comparison, or returns
    ///     <see langword="null" /> when the operand is not a complex-property access so EF's
    ///     narrowed comparison is kept unchanged.
    /// </summary>
    private DuckDBStructPresenceCheckExpression? CreateStructPresenceCheck(
        BinaryExpression binaryExpression,
        SqlExpression visitedSql)
    {
        // Determine the checked complex property's nesting depth below its struct root from the
        // operand member chain: c.Location == null -> depth 0; c.Location.Address == null -> depth 1.
        var left = RemoveImplicitConvert(binaryExpression.Left);
        var right = RemoveImplicitConvert(binaryExpression.Right);
        var operand = left is ConstantExpression { Value: null } ? right : left;
        var depth = GetComplexPropertyChainDepth(operand) - 1;

        if (depth < 0)
        {
            return null;
        }

        // Resolve the checked complex property's configured struct root so the rewrite targets
        // that root rather than an arbitrary overridden leaf root (see HasStructField).
        var (structColumnName, fieldPath) = ResolveStructRoot(operand, depth);
        return new DuckDBStructPresenceCheckExpression(
            binaryExpression.NodeType,
            visitedSql,
            depth,
            structColumnName,
            fieldPath);
    }

    /// <summary>
    ///     Resolves the physical struct root column and the field path to the checked complex from
    ///     the operand member chain, using the complex property's immutable struct mapping. Returns
    ///     <see langword="null" /> root when the operand is not a struct-mapped complex-property
    ///     access, in which case the rewrite keeps EF's narrowed comparison.
    /// </summary>
    private (string? StructColumnName, IReadOnlyList<string> FieldPath) ResolveStructRoot(
        Expression operand,
        int depth)
    {
        var members = new List<MemberExpression>();
        var current = RemoveImplicitConvert(operand);
        while (current is MemberExpression { Expression: { } inner } member)
        {
            members.Add(member);
            current = RemoveImplicitConvert(inner);
        }

        // members is outermost-first; reverse to walk innermost-first.
        members.Reverse();

        ITypeBase? structuralType = current switch
        {
            ParameterExpression parameter => _model.FindEntityType(parameter.Type),
            RelationalStructuralTypeShaperExpression shaper => shaper.StructuralType,
            _ => null
        };

        if (structuralType is null)
        {
            return (null, []);
        }

        var complexProperties = new List<IReadOnlyComplexProperty>(members.Count);
        foreach (var member in members)
        {
            var complexProperty = structuralType.FindComplexProperty(member.Member.Name);
            if (complexProperty is null)
            {
                return (null, []);
            }

            complexProperties.Add(complexProperty);
            structuralType = complexProperty.ComplexType;
        }

        if (complexProperties.Count == 0)
        {
            return (null, []);
        }

        var rootMapping = complexProperties[0].GetStructMapping();
        if (rootMapping is null)
        {
            return (null, []);
        }

        // The field path to the checked complex is the struct field name of every complex below
        // the root. The root itself contributes no path segment.
        var fieldPath = new List<string>(depth);
        for (var i = 1; i < complexProperties.Count; i++)
        {
            var nestedMapping = complexProperties[i].GetStructMapping();
            if (nestedMapping?.FieldName is not { } fieldName)
            {
                return (null, []);
            }

            fieldPath.Add(fieldName);
        }

        return (rootMapping.StructColumnName, fieldPath);
    }

    /// <summary>
    ///     Counts the member expressions in the operand chain, or returns -1 when the operand is
    ///     not the expected complex-property access shape.
    /// </summary>
    private static int GetComplexPropertyChainDepth(Expression expression)
    {
        var depth = 0;
        var current = expression;
        while (true)
        {
            current = RemoveImplicitConvert(current);
            switch (current)
            {
                case MemberExpression { Expression: { } inner } member:
                    depth++;
                    current = inner;
                    break;
                case ParameterExpression:
                case RelationalStructuralTypeShaperExpression:
                case null:
                    return depth;
                default:
                    return -1;
            }
        }
    }

    private IEnumerable<IComplexType> EnumerateComplexTypes(IModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var complexProperty in entityType.GetComplexProperties())
            {
                foreach (var complexType in EnumerateComplexTypes(complexProperty.ComplexType))
                {
                    yield return complexType;
                }
            }
        }
    }

    private static IEnumerable<IComplexType> EnumerateComplexTypes(IComplexType complexType)
    {
        yield return complexType;
        foreach (var nestedComplexProperty in complexType.GetComplexProperties())
        {
            foreach (var nestedType in EnumerateComplexTypes(nestedComplexProperty.ComplexType))
            {
                yield return nestedType;
            }
        }
    }

    private static Expression RemoveImplicitConvert(Expression expression)
        => expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary
            ? RemoveImplicitConvert(unary.Operand)
            : expression;

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
