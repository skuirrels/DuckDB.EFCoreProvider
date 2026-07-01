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
public class DuckDBObjectToStringTranslator : IMethodCallTranslator
{
    private static readonly HashSet<Type> SupportedTypes =
    [
        typeof(bool),
        typeof(byte),
        typeof(char),
        typeof(DateTime),
        typeof(DateOnly),
        typeof(DateTimeOffset),
        typeof(decimal),
        typeof(double),
        typeof(float),
        typeof(Guid),
        typeof(int),
        typeof(long),
        typeof(sbyte),
        typeof(short),
        typeof(TimeOnly),
        typeof(TimeSpan),
        typeof(uint),
        typeof(ulong),
        typeof(ushort)
    ];

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly RelationalTypeMapping _varcharTypeMapping;

    public DuckDBObjectToStringTranslator(IRelationalTypeMappingSource typeMappingSource, ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _varcharTypeMapping = typeMappingSource.FindMapping(typeof(string))!;
    }

    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is null || method.Name != nameof(ToString) || arguments.Count != 0)
        {
            return null;
        }

        var instanceType = instance.Type.UnwrapNullableType();

        if (!SupportedTypes.Contains(instanceType))
        {
            return null;
        }

        if (instanceType == typeof(bool))
        {
            var trueConst  = _sqlExpressionFactory.Constant(bool.TrueString,  _varcharTypeMapping);
            var falseConst = _sqlExpressionFactory.Constant(bool.FalseString, _varcharTypeMapping);
            var emptyConst = _sqlExpressionFactory.Constant(string.Empty, _varcharTypeMapping);

            var couldBeNull = instance.Type.IsNullableType()
                || instance is ColumnExpression { IsNullable: true }
                || instance is not ColumnExpression;

            if (couldBeNull)
            {
                var caseExpr = _sqlExpressionFactory.Case(
                    [
                        new CaseWhenClause(_sqlExpressionFactory.Equal(instance, _sqlExpressionFactory.Constant(true)), trueConst),
                        new CaseWhenClause(_sqlExpressionFactory.Equal(instance, _sqlExpressionFactory.Constant(false)), falseConst),
                    ],
                    elseResult: null);

                return _sqlExpressionFactory.Coalesce(caseExpr, emptyConst, _varcharTypeMapping);
            }

            return _sqlExpressionFactory.Case(
                [new CaseWhenClause(_sqlExpressionFactory.Equal(instance, _sqlExpressionFactory.Constant(true)), trueConst)],
                falseConst);
        }

        var converted = _sqlExpressionFactory.Convert(instance, typeof(string), _varcharTypeMapping);

        return _sqlExpressionFactory.Coalesce(
            converted,
            _sqlExpressionFactory.Constant(string.Empty, _varcharTypeMapping),
            _varcharTypeMapping);
    }
}
