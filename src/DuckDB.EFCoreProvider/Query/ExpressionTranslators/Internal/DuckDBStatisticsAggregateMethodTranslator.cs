using DuckDB.EFCoreProvider.Extensions.DbFunctionsExtensions;
using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.ExpressionTranslators.Internal;

/// <summary>Translates selected DuckDB statistical and argument aggregates.</summary>
internal sealed class DuckDBStatisticsAggregateMethodTranslator : IAggregateMethodCallTranslator
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly RelationalTypeMapping _doubleTypeMapping;

    public DuckDBStatisticsAggregateMethodTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _doubleTypeMapping = typeMappingSource.FindMapping(typeof(double))!;
    }

    public SqlExpression? Translate(
        MethodInfo method,
        EnumerableExpression source,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method.DeclaringType != typeof(DuckDBDbFunctionsExtensions))
        {
            return null;
        }

        if (method.Name == nameof(DuckDBDbFunctionsExtensions.StandardDeviationSample)
            && source.Selector is SqlExpression selector)
        {
            selector = CombineTerms(source, selector);
            return _sqlExpressionFactory.Function(
                "stddev_samp",
                [selector],
                nullable: true,
                argumentsPropagateNullability: [false],
                typeof(double),
                _doubleTypeMapping);
        }

        if (method.Name is nameof(DuckDBDbFunctionsExtensions.ArgMax) or nameof(DuckDBDbFunctionsExtensions.ArgMin)
            && source.Selector is DuckDBRowValueExpression { Values.Count: 2 } row)
        {
            if (source.IsDistinct)
            {
                return null;
            }

            var value = row.Values[0];
            var order = row.Values[1];
            if (source.Predicate is not null)
            {
                value = _sqlExpressionFactory.Case(
                    [new CaseWhenClause(source.Predicate, value)],
                    elseResult: null);
                order = _sqlExpressionFactory.Case(
                    [new CaseWhenClause(source.Predicate, order)],
                    elseResult: null);
            }

            return _sqlExpressionFactory.Function(
                method.Name == nameof(DuckDBDbFunctionsExtensions.ArgMax) ? "arg_max" : "arg_min",
                [value, order],
                nullable: true,
                argumentsPropagateNullability: [false, false],
                value.Type,
                value.TypeMapping);
        }

        return null;
    }

    private SqlExpression CombineTerms(EnumerableExpression source, SqlExpression expression)
    {
        if (source.Predicate is not null)
        {
            expression = _sqlExpressionFactory.Case(
                [new CaseWhenClause(source.Predicate, expression)],
                elseResult: null);
        }

        return source.IsDistinct ? new DistinctExpression(expression) : expression;
    }
}