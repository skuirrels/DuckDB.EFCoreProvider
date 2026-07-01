using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.ExpressionTranslators.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBMathTranslator : IMethodCallTranslator
{
    private static readonly Dictionary<MethodInfo, string> SupportedMethods = new()
    {
        { typeof(Math).GetMethod(nameof(Math.Abs), [typeof(decimal)])!, "abs" },
        { typeof(Math).GetMethod(nameof(Math.Abs), [typeof(double)])!, "abs" },
        { typeof(Math).GetMethod(nameof(Math.Abs), [typeof(short)])!, "abs" },
        { typeof(Math).GetMethod(nameof(Math.Abs), [typeof(int)])!, "abs" },
        { typeof(Math).GetMethod(nameof(Math.Abs), [typeof(long)])!, "abs" },
        { typeof(Math).GetMethod(nameof(Math.Abs), [typeof(sbyte)])!, "abs" },
        { typeof(Math).GetMethod(nameof(Math.Abs), [typeof(float)])!, "abs" },

        { typeof(MathF).GetMethod(nameof(MathF.Acos), [typeof(float)])!, "acos" },
        { typeof(Math).GetMethod(nameof(Math.Acos), [typeof(double)])!, "acos" },

        { typeof(MathF).GetMethod(nameof(MathF.Acosh), [typeof(float)])!, "acosh" },
        { typeof(Math).GetMethod(nameof(Math.Acosh), [typeof(double)])!, "acosh" },

        { typeof(MathF).GetMethod(nameof(MathF.Asin), [typeof(float)])!, "asin" },
        { typeof(Math).GetMethod(nameof(Math.Asin), [typeof(double)])!, "asin" },

        { typeof(MathF).GetMethod(nameof(MathF.Asinh), [typeof(float)])!, "asinh" },
        { typeof(Math).GetMethod(nameof(Math.Asinh), [typeof(double)])!, "asinh" },

        { typeof(MathF).GetMethod(nameof(MathF.Atan), [typeof(float)])!, "atan" },
        { typeof(Math).GetMethod(nameof(Math.Atan), [typeof(double)])!, "atan" },

        { typeof(MathF).GetMethod(nameof(MathF.Atanh), [typeof(float)])!, "atanh" },
        //{ typeof(Math).GetMethod(nameof(Math.Atanh), [typeof(double)])!, "atanh" },

        { typeof(MathF).GetMethod(nameof(MathF.Atan2), [typeof(float), typeof(float)])!, "atan2" },
        { typeof(Math).GetMethod(nameof(Math.Atan2), [typeof(double), typeof(double)])!, "atan2" },

        { typeof(MathF).GetMethod(nameof(MathF.Cbrt), [typeof(float)])!, "cbrt" },
        { typeof(Math).GetMethod(nameof(Math.Cbrt), [typeof(double)])!, "cbrt" },

        { typeof(MathF).GetMethod(nameof(MathF.Ceiling), [typeof(float)])!, "ceiling" },
        { typeof(Math).GetMethod(nameof(Math.Ceiling), [typeof(double)])!, "ceiling" },

        { typeof(MathF).GetMethod(nameof(MathF.Cos), [typeof(float)])!, "cos" },
        { typeof(Math).GetMethod(nameof(Math.Cos), [typeof(double)])!, "cos" },

        { typeof(MathF).GetMethod(nameof(MathF.Cosh), [typeof(float)])!, "cosh" },
        { typeof(Math).GetMethod(nameof(Math.Cosh), [typeof(double)])!, "cosh" },

        { typeof(float).GetMethod(nameof(float.RadiansToDegrees), [typeof(float)])!, "degrees" },
        { typeof(double).GetMethod(nameof(double.RadiansToDegrees), [typeof(double)])!, "degrees" },

        { typeof(MathF).GetMethod(nameof(MathF.Exp), [typeof(float)])!, "exp" },
        { typeof(Math).GetMethod(nameof(Math.Exp), [typeof(double)])!, "exp" },

        { typeof(MathF).GetMethod(nameof(MathF.Floor), [typeof(float)])!, "floor" },
        { typeof(Math).GetMethod(nameof(Math.Floor), [typeof(double)])!, "floor" },
        { typeof(Math).GetMethod(nameof(Math.Floor), [typeof(decimal)])!, "floor" },

        { typeof(MathF).GetMethod(nameof(MathF.Log), [typeof(float)])!, "ln" },
        { typeof(Math).GetMethod(nameof(Math.Log), [typeof(double)])!, "ln" },

        { typeof(MathF).GetMethod(nameof(MathF.Log10), [typeof(float)])!, "log10" },
        { typeof(Math).GetMethod(nameof(Math.Log10), [typeof(double)])!, "log10" },

        { typeof(MathF).GetMethod(nameof(MathF.Log2), [typeof(float)])!, "log2" },
        { typeof(Math).GetMethod(nameof(Math.Log2), [typeof(double)])!, "log2" },

        { typeof(MathF).GetMethod(nameof(MathF.Pow), [typeof(float), typeof(float)])!, "pow" },
        { typeof(Math).GetMethod(nameof(Math.Pow), [typeof(double), typeof(double)])!, "pow" },

        { typeof(float).GetMethod(nameof(float.DegreesToRadians), [typeof(float)])!, "radians" },
        { typeof(double).GetMethod(nameof(double.DegreesToRadians), [typeof(double)])!, "radians" },

        { typeof(Math).GetMethod(nameof(Math.Sign), [typeof(decimal)])!, "sign" },
        { typeof(Math).GetMethod(nameof(Math.Sign), [typeof(double)])!, "sign" },
        { typeof(Math).GetMethod(nameof(Math.Sign), [typeof(short)])!, "sign" },
        { typeof(Math).GetMethod(nameof(Math.Sign), [typeof(int)])!, "sign" },
        { typeof(Math).GetMethod(nameof(Math.Sign), [typeof(long)])!, "sign" },
        { typeof(Math).GetMethod(nameof(Math.Sign), [typeof(sbyte)])!, "sign" },
        { typeof(Math).GetMethod(nameof(Math.Sign), [typeof(float)])!, "sign" },

        { typeof(MathF).GetMethod(nameof(MathF.Sin), [typeof(float)])!, "sin" },
        { typeof(Math).GetMethod(nameof(Math.Sin), [typeof(double)])!, "sin" },

        { typeof(MathF).GetMethod(nameof(MathF.Sinh), [typeof(float)])!, "sinh" },
        { typeof(Math).GetMethod(nameof(Math.Sinh), [typeof(double)])!, "sinh" },

        { typeof(MathF).GetMethod(nameof(MathF.Sqrt), [typeof(float)])!, "sqrt" },
        { typeof(Math).GetMethod(nameof(Math.Sqrt), [typeof(double)])!, "sqrt" },

        { typeof(MathF).GetMethod(nameof(MathF.Tan), [typeof(float)])!, "tan" },
        { typeof(Math).GetMethod(nameof(Math.Tan), [typeof(double)])!, "tan" },
        
        { typeof(MathF).GetMethod(nameof(MathF.Tanh), [typeof(float)])!, "tanh" },
        { typeof(Math).GetMethod(nameof(Math.Tanh), [typeof(double)])!, "tanh" },
        
        { typeof(Math).GetMethod(nameof(Math.Truncate), [typeof(decimal)])!, "trunc" },
        { typeof(Math).GetMethod(nameof(Math.Truncate), [typeof(double)])!, "trunc" },
        { typeof(MathF).GetMethod(nameof(MathF.Truncate), [typeof(float)])!, "trunc" },
    };

    private static readonly MethodInfo AtanhDouble = typeof(Math).GetMethod(nameof(Math.Atanh), [typeof(double)])!;

    private static readonly MethodInfo LogFloatNewBase = typeof(MathF).GetMethod(nameof(MathF.Log), [typeof(float), typeof(float)])!;
    private static readonly MethodInfo LogDoubleNewBase = typeof(Math).GetMethod(nameof(Math.Log), [typeof(double), typeof(double)])!;

    private static readonly MethodInfo RoundDecimal = typeof(Math).GetMethod(nameof(Math.Round), [typeof(decimal)])!;
    private static readonly MethodInfo RoundDouble = typeof(Math).GetMethod(nameof(Math.Round), [typeof(float)])!;
    private static readonly MethodInfo RoundFloat = typeof(MathF).GetMethod(nameof(MathF.Round), [typeof(float)])!;

    private static readonly MethodInfo RoundDecimalWithDecimals = typeof(Math).GetMethod(nameof(Math.Round), [typeof(decimal), typeof(int)])!;
    private static readonly MethodInfo RoundDoubleWithDecimals = typeof(Math).GetMethod(nameof(Math.Round), [typeof(double), typeof(int)])!;
    private static readonly MethodInfo RoundFloatWithDecimals = typeof(MathF).GetMethod(nameof(MathF.Round), [typeof(float), typeof(int)])!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public DuckDBMathTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
    }

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (SupportedMethods.TryGetValue(method, out var sqlFunctionName))
        {
            var typeMapping = ExpressionExtensions.InferTypeMapping(arguments.ToArray());
            var newArguments = arguments
                .Select(a => _sqlExpressionFactory.ApplyTypeMapping(a, typeMapping))
                .ToList();

            var mathFunction = _sqlExpressionFactory.Function(
                sqlFunctionName,
                newArguments,
                nullable: true,
                argumentsPropagateNullability: newArguments.Select(_ => true).ToList(),
                method.ReturnType,
                typeMapping);

            var isNan = _sqlExpressionFactory.Function(
                name: "isnan",
                arguments: [mathFunction],
                nullable: false,
                argumentsPropagateNullability: [true],
                typeof(bool));

            return _sqlExpressionFactory.Case(
                [new CaseWhenClause(isNan, _sqlExpressionFactory.Constant(null, method.ReturnType))],
                mathFunction);
        }

        if (method == AtanhDouble)
        {
            return TranslateAtanh(arguments[0]);
        }

        if (method == LogFloatNewBase || method == LogDoubleNewBase)
        {
            return TranslateLogNewBase(arguments[0], arguments[1], method.ReturnType);
        }

        if (method == RoundDecimal || method == RoundDouble || method == RoundFloat)
        {
            return TranslateRound(arguments[0], _sqlExpressionFactory.Constant(0), method.ReturnType);
        }

        if (method == RoundDecimalWithDecimals || method == RoundDoubleWithDecimals || method == RoundFloatWithDecimals)
        {
            return TranslateRound(arguments[0], arguments[1], method.ReturnType);
        }

        return null;
    }

    private SqlExpression TranslateAtanh(SqlExpression argument)
    {
        var atanh = _sqlExpressionFactory.Function(
            name: "atanh",
            arguments: [argument],
            nullable: false,
            argumentsPropagateNullability: [true],
            typeof(double));

        var minusOne = _sqlExpressionFactory.Constant(-1.0, typeof(double));
        var one = _sqlExpressionFactory.Constant(1.0, typeof(double));
        var greaterOrEqualMinusOne = _sqlExpressionFactory.GreaterThanOrEqual(argument, minusOne);
        var lessOrEqualOne = _sqlExpressionFactory.LessThanOrEqual(argument, one);
        var between = _sqlExpressionFactory.AndAlso(greaterOrEqualMinusOne, lessOrEqualOne);

        return _sqlExpressionFactory.Case(
            [new CaseWhenClause(between, atanh)],
            _sqlExpressionFactory.Constant(null, typeof(double))
        );
    }

    private SqlExpression TranslateLogNewBase(SqlExpression x, SqlExpression y, Type returnType)
    {
        var function1 = _sqlExpressionFactory.Function(
            name: "ln",
            arguments: [x],
            nullable: false,
            argumentsPropagateNullability: [false],
            returnType: returnType);

        var function2 = _sqlExpressionFactory.Function(
            name: "ln",
            arguments: [y],
            nullable: false,
            argumentsPropagateNullability: [false],
            returnType: returnType);

        return _sqlExpressionFactory.Divide(function1, function2);
    }

    private SqlExpression TranslateRound(SqlExpression v, SqlExpression s, Type returnType)
    {
        return _sqlExpressionFactory.Function(
            name: "round",
            arguments: [v, s],
            nullable: true,
            argumentsPropagateNullability: [true, true],
            returnType: returnType);
    }
}
