using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.ExpressionTranslators.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBStringMethodTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo StartsWith = typeof(string).GetRuntimeMethod(nameof(string.StartsWith), [typeof(string)])!;
    private static readonly MethodInfo StartsWithChar = typeof(string).GetRuntimeMethod(nameof(string.StartsWith), [typeof(char)])!;
    private static readonly MethodInfo Contains = typeof(string).GetRuntimeMethod(nameof(string.Contains), [typeof(string)])!;
    private static readonly MethodInfo ContainsChar = typeof(string).GetRuntimeMethod(nameof(string.Contains), [typeof(char)])!;
    private static readonly MethodInfo EndsWith = typeof(string).GetRuntimeMethod(nameof(string.EndsWith), [typeof(string)])!;
    private static readonly MethodInfo EndsWithChar = typeof(string).GetRuntimeMethod(nameof(string.EndsWith), [typeof(char)])!;
    private static readonly MethodInfo Substring = typeof(string).GetRuntimeMethod(nameof(string.Substring), [typeof(int)])!;
    private static readonly MethodInfo SubstringLength = typeof(string).GetRuntimeMethod(nameof(string.Substring), [typeof(int), typeof(int)])!;
    private static readonly MethodInfo ToUpper = typeof(string).GetRuntimeMethod(nameof(string.ToUpper), Type.EmptyTypes)!;
    private static readonly MethodInfo ToLower = typeof(string).GetRuntimeMethod(nameof(string.ToLower), Type.EmptyTypes)!;
    private static readonly MethodInfo Trim = typeof(string).GetRuntimeMethod(nameof(string.Trim), Type.EmptyTypes)!;
    private static readonly MethodInfo TrimWithChar = typeof(string).GetRuntimeMethod(nameof(string.Trim), [typeof(char)])!;
    private static readonly MethodInfo TrimWithChars = typeof(string).GetRuntimeMethod(nameof(string.Trim), [typeof(char[])])!;
    private static readonly MethodInfo TrimStart = typeof(string).GetRuntimeMethod(nameof(string.TrimStart), Type.EmptyTypes)!;
    private static readonly MethodInfo TrimStartWithChar = typeof(string).GetRuntimeMethod(nameof(string.TrimStart), [typeof(char)])!;
    private static readonly MethodInfo TrimStartWithCharArray = typeof(string).GetRuntimeMethod(nameof(string.TrimStart), [typeof(char[])])!;
    private static readonly MethodInfo TrimEnd = typeof(string).GetRuntimeMethod(nameof(string.TrimEnd), Type.EmptyTypes)!;
    private static readonly MethodInfo TrimEndWithChar = typeof(string).GetRuntimeMethod(nameof(string.TrimEnd), [typeof(char)])!;
    private static readonly MethodInfo TrimEndWithCharArray = typeof(string).GetRuntimeMethod(nameof(string.TrimEnd), [typeof(char[])])!;
    private static readonly MethodInfo IndexOf = typeof(string).GetRuntimeMethod(nameof(string.IndexOf), [typeof(string)])!;
    private static readonly MethodInfo IndexOfChar = typeof(string).GetRuntimeMethod(nameof(string.IndexOf), [typeof(char)])!;
    private static readonly MethodInfo IndexOfWithPosition = typeof(string).GetRuntimeMethod(nameof(string.IndexOf), [typeof(string), typeof(int)])!;
    private static readonly MethodInfo IndexOfCharWithPosition = typeof(string).GetRuntimeMethod(nameof(string.IndexOf), [typeof(char), typeof(int)])!;
    private static readonly MethodInfo Replace = typeof(string).GetRuntimeMethod(nameof(string.Replace), [typeof(string), typeof(string)])!;
    private static readonly MethodInfo ReplaceChar = typeof(string).GetRuntimeMethod(nameof(string.Replace), [typeof(char), typeof(char)])!;
    private static readonly MethodInfo IsNullOrEmpty = typeof(string).GetRuntimeMethod(nameof(string.IsNullOrEmpty), [typeof(string)])!;
    private static readonly MethodInfo IsNullOrWhiteSpace = typeof(string).GetRuntimeMethod(nameof(string.IsNullOrWhiteSpace), [typeof(string)])!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly ITypeMappingSource _typeMappingSource;
    private RelationalTypeMapping? _boolTypeMapping;
    private RelationalTypeMapping? _charTypeMapping;

    public DuckDBStringMethodTranslator(ISqlExpressionFactory sqlExpressionFactory, ITypeMappingSource typeMappingSource)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _typeMappingSource = typeMappingSource;
    }

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method == StartsWith || method == StartsWithChar)
        {
            _boolTypeMapping ??= (RelationalTypeMapping)_typeMappingSource.FindMapping(typeof(bool))!;

            var startsWithFunction = _sqlExpressionFactory.Function(
                name: "starts_with",
                arguments: [instance!, arguments[0]],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                returnType: typeof(bool),
                typeMapping: _boolTypeMapping);

            return _sqlExpressionFactory.Coalesce(
                startsWithFunction,
                _sqlExpressionFactory.Constant(false, typeof(bool), _boolTypeMapping),
                _boolTypeMapping);
        }

        if (method == Contains || method == ContainsChar)
        {
            _boolTypeMapping ??= (RelationalTypeMapping)_typeMappingSource.FindMapping(typeof(bool))!;

            var containsFunction = _sqlExpressionFactory.Function(
                name: "contains",
                arguments: [instance!, arguments[0]],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                returnType: typeof(bool),
                typeMapping: _boolTypeMapping);

            return _sqlExpressionFactory.Coalesce(
                containsFunction,
                _sqlExpressionFactory.Constant(false, typeof(bool), _boolTypeMapping),
                _boolTypeMapping);
        }

        if (method == EndsWith || method == EndsWithChar)
        {
            _boolTypeMapping ??= (RelationalTypeMapping)_typeMappingSource.FindMapping(typeof(bool))!;

            var endsWithFunction = _sqlExpressionFactory.Function(
                name: "ends_with",
                arguments: [instance!, arguments[0]],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                returnType: typeof(bool),
                typeMapping: _boolTypeMapping);

            return _sqlExpressionFactory.Coalesce(
                endsWithFunction,
                _sqlExpressionFactory.Constant(false, typeof(bool), _boolTypeMapping),
                _boolTypeMapping);
        }

        if (method == Substring)
        {
            return _sqlExpressionFactory.Function(
                name: "substring",
                arguments: [instance!, _sqlExpressionFactory.Add(arguments[0], _sqlExpressionFactory.Constant(1))],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                returnType: typeof(string));
        }

        if (method == SubstringLength)
        {
            return _sqlExpressionFactory.Function(
                name: "substring",
                arguments: [instance!, _sqlExpressionFactory.Add(arguments[0], _sqlExpressionFactory.Constant(1)), arguments[1]],
                nullable: true,
                argumentsPropagateNullability: [true, true, true],
                returnType: typeof(string));
        }

        if (method == ToUpper)
        {
            return _sqlExpressionFactory.Function(
                name: "upper",
                arguments: [instance!],
                nullable: true,
                argumentsPropagateNullability: [true],
                returnType: typeof(string));
        }

        if (method == ToLower)
        {
            return _sqlExpressionFactory.Function(
                name: "lower",
                arguments: [instance!],
                nullable: true,
                argumentsPropagateNullability: [true],
                returnType: typeof(string));
        }

        if (method == Trim)
        {
            return _sqlExpressionFactory.Function(
                name: "trim",
                arguments: [instance!],
                nullable: true,
                argumentsPropagateNullability: [true],
                returnType: typeof(string));
        }

        if (method == TrimWithChar)
        {
            return _sqlExpressionFactory.Function(
                name: "trim",
                arguments: [instance!, arguments[0]],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                returnType: typeof(string));
        }

        if (method == TrimWithChars && arguments[0] is SqlConstantExpression { Value: char[] } trimChars)
        {
            return _sqlExpressionFactory.Function(
                name: "trim",
                arguments: [instance!, _sqlExpressionFactory.Constant(string.Join("", (char[])trimChars.Value), typeof(string))],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                returnType: typeof(string));
        }

        if (method == IndexOf || method == IndexOfChar)
        {
            return _sqlExpressionFactory.Subtract(_sqlExpressionFactory.Function(
                    name: "instr",
                    arguments: [instance!, arguments[0]],
                    nullable: true,
                    argumentsPropagateNullability: [true, true],
                    returnType: typeof(int)),
                _sqlExpressionFactory.Constant(1));
        }

        if (method == IndexOfWithPosition || method == IndexOfCharWithPosition)
        {
            var substringFromStart = _sqlExpressionFactory.Function(
                name: "substring",
                arguments: [instance!, _sqlExpressionFactory.Add(arguments[1], _sqlExpressionFactory.Constant(1))],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                returnType: typeof(string));

            var instrResult = _sqlExpressionFactory.Function(
                name: "instr",
                arguments: [substringFromStart, arguments[0]],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                returnType: typeof(int));

            return _sqlExpressionFactory.Add(
                _sqlExpressionFactory.Subtract(instrResult, _sqlExpressionFactory.Constant(1)),
                arguments[1]);
        }
        
        if (method == TrimStart)
        {
            return _sqlExpressionFactory.Function(
                name: "ltrim",
                arguments: [instance!],
                nullable: true,
                argumentsPropagateNullability: [true],
                returnType: typeof(string));
        }

        if (method == TrimStartWithChar)
        {
            return _sqlExpressionFactory.Function(
                name: "ltrim",
                arguments: [instance!, arguments[0]],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                returnType: typeof(string));
        }

        if (method == TrimStartWithCharArray && arguments[0] is SqlConstantExpression { Value: char[] } constantExpression)
        {
            var stringValue = string.Join("", (char[])constantExpression.Value);

            return _sqlExpressionFactory.Function(
                name: "ltrim",
                arguments: [instance!, _sqlExpressionFactory.Constant(stringValue, typeof(string))],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                returnType: typeof(string));
        }

        if (method == TrimEnd)
        {
            return _sqlExpressionFactory.Function(
                name: "rtrim",
                arguments: [instance!],
                nullable: true,
                argumentsPropagateNullability: [true],
                returnType: typeof(string));
        }

        if (method == TrimEndWithChar)
        {
            return _sqlExpressionFactory.Function(
                name: "rtrim",
                arguments: [instance!, arguments[0]],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                returnType: typeof(string));
        }

        if (method == TrimEndWithCharArray && arguments[0] is SqlConstantExpression { Value: char[] } trimEndChars)
        {
            var stringValue = string.Join("", (char[])trimEndChars.Value);

            return _sqlExpressionFactory.Function(
                name: "rtrim",
                arguments: [instance!, _sqlExpressionFactory.Constant(stringValue, typeof(string))],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                returnType: typeof(string));
        }

        if (method == TrimStartWithChar)
        {
            return _sqlExpressionFactory.Function(
                name: "ltrim",
                arguments: [instance!, arguments[0]],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                returnType: typeof(string));
        }

        if (method == Replace || method == ReplaceChar)
        {
            return _sqlExpressionFactory.Function(
                name: "replace",
                arguments: [instance!, arguments[0], arguments[1]],
                nullable: true,
                argumentsPropagateNullability: [true, true, true],
                returnType: typeof(string));
        }

        if (method == IsNullOrEmpty)
        {
            return _sqlExpressionFactory.OrElse(
                _sqlExpressionFactory.IsNull(arguments[0]),
                _sqlExpressionFactory.Equal(
                    _sqlExpressionFactory.Function(
                        name: "length",
                        arguments:
                        [
                            _sqlExpressionFactory.Convert(arguments[0], typeof(string))
                        ],
                        argumentsPropagateNullability: [true],
                        nullable: true,
                        returnType: typeof(int)),
                    _sqlExpressionFactory.Constant(0))
            );
        }

        if (method == IsNullOrWhiteSpace)
        {
            return _sqlExpressionFactory.OrElse(
                _sqlExpressionFactory.IsNull(arguments[0]),
                _sqlExpressionFactory.Equal(
                    _sqlExpressionFactory.Function(
                        name: "length",
                        arguments:
                        [
                            _sqlExpressionFactory.Convert(
                                _sqlExpressionFactory.Function(
                                    name: "trim",
                                    arguments: [arguments[0]],
                                    nullable: true,
                                    argumentsPropagateNullability: [true],
                                    returnType: typeof(string)),
                                typeof(string))
                        ],
                        argumentsPropagateNullability: [true],
                        nullable: true,
                        returnType: typeof(int)),
                    _sqlExpressionFactory.Constant(0))
            );
        }

        if (method.Name == nameof(Enumerable.FirstOrDefault) &&
            method.DeclaringType == typeof(Enumerable) &&
            arguments is [{ Type: var firstArgType }] && firstArgType == typeof(string))
        {
            _charTypeMapping ??= (RelationalTypeMapping?)_typeMappingSource.FindMapping(typeof(char));

            return _sqlExpressionFactory.Function(
                name: "left",
                arguments: [arguments[0], _sqlExpressionFactory.Constant(1)],
                nullable: true,
                argumentsPropagateNullability: [true, false],
                returnType: typeof(char),
                typeMapping: _charTypeMapping);
        }

        if (method.Name == nameof(Enumerable.LastOrDefault) &&
            method.DeclaringType == typeof(Enumerable) &&
            arguments is [{ Type: var lastArgType }] && lastArgType == typeof(string))
        {
            _charTypeMapping ??= (RelationalTypeMapping?)_typeMappingSource.FindMapping(typeof(char));

            return _sqlExpressionFactory.Function(
                name: "right",
                arguments: [arguments[0], _sqlExpressionFactory.Constant(1)],
                nullable: true,
                argumentsPropagateNullability: [true, false],
                returnType: typeof(char),
                typeMapping: _charTypeMapping);
        }
        
        return null;
    }
}
