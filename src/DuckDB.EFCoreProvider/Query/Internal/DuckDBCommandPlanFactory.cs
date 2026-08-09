using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Extensions.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Internal;

internal sealed class DuckDBCommandPlanFactory(
    ICurrentDbContext currentDbContext,
    IQueryContextFactory queryContextFactory,
    IQueryCompilationContextFactory queryCompilationContextFactory,
    IAsyncQueryProvider queryProvider,
    IQueryCompiler queryCompiler,
    IDiagnosticsLogger<DbLoggerCategory.Query> queryLogger,
    ILiftableConstantProcessor liftableConstantProcessor)
{
    private delegate DuckDBCommandPlan ScalarPlanCreator(
        DuckDBCommandPlanFactory factory,
        Expression expression);

    private static readonly MethodInfo CreateScalarCoreMethod = typeof(DuckDBCommandPlanFactory)
        .GetMethod(nameof(CreateScalarCore), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly ConcurrentDictionary<Type, ScalarPlanCreator> AveragePlanCreators = new();

    public DuckDBCommandPlan Create<T>(IQueryable<T> query)
    {
        ValidateQuery(query);
        var enumerable = query.Provider.Execute<IEnumerable<T>>(query.Expression);
        return Create(enumerable);
    }

    public DuckDBCommandPlan CreateCount<T>(IQueryable<T> query)
        => CreateScalar<int, T>(query, DuckDBTerminalQueryOperator.Count);

    public DuckDBCommandPlan CreateLongCount<T>(IQueryable<T> query)
        => CreateScalar<long, T>(query, DuckDBTerminalQueryOperator.LongCount);

    public DuckDBCommandPlan CreateAny<T>(IQueryable<T> query)
        => CreateScalar<bool, T>(query, DuckDBTerminalQueryOperator.Any);

    public DuckDBCommandPlan CreateMin<T>(IQueryable<T> query)
        => CreateScalar<T, T>(query, DuckDBTerminalQueryOperator.Min);

    public DuckDBCommandPlan CreateMax<T>(IQueryable<T> query)
        => CreateScalar<T, T>(query, DuckDBTerminalQueryOperator.Max);

    public DuckDBCommandPlan CreateSum<T>(IQueryable<T> query)
        => CreateScalar<T, T>(query, DuckDBTerminalQueryOperator.Sum);

    public DuckDBCommandPlan CreateAverage<T>(IQueryable<T> query)
        => CreateAverageWithResolvedResult(query);

    private DuckDBCommandPlan CreateScalar<TResult, T>(
        IQueryable<T> query,
        DuckDBTerminalQueryOperator queryOperator)
    {
        ValidateQuery(query);

        var method = DuckDBTerminalQueryMethodResolver.Resolve(queryOperator, typeof(T));
        var expression = Expression.Call(method, query.Expression);

        return CreateScalarCore<TResult>(expression);
    }

    private DuckDBCommandPlan CreateAverageWithResolvedResult<T>(IQueryable<T> query)
    {
        ValidateQuery(query);

        var method = DuckDBTerminalQueryMethodResolver.Resolve(DuckDBTerminalQueryOperator.Average, typeof(T));
        var expression = Expression.Call(method, query.Expression);
        var creator = AveragePlanCreators.GetOrAdd(
            expression.Type,
            static resultType => CreateScalarCoreMethod
                .MakeGenericMethod(resultType)
                .CreateDelegate<ScalarPlanCreator>());

        return creator(this, expression);
    }

    private DuckDBCommandPlan CreateScalarCore<TResult>(Expression expression)
    {
        if (queryCompiler is not QueryCompiler compiler)
        {
            throw new NotSupportedException(
                "DuckDB command-plan extraction requires EF Core's standard query compiler.");
        }

        var queryContext = queryContextFactory.Create();
        var parameterized = compiler.ExtractParameters(expression, queryContext.Parameters, queryLogger);
        var compilationContext = queryCompilationContextFactory.Create(async: false);
        var compiled = compilationContext.CreateQueryExecutorExpression<TResult>(parameterized);
        var inlined = (Expression<Func<QueryContext, TResult>>)liftableConstantProcessor.InlineConstants(
            compiled,
            compilationContext.SupportsPrecompiledQuery);
        var queryingEnumerable = new QueryingEnumerableFinder().Find(inlined.Body);
        var factory = Expression.Lambda<Func<QueryContext, IRelationalQueryingEnumerable>>(
            Expression.Convert(queryingEnumerable, typeof(IRelationalQueryingEnumerable)),
            inlined.Parameters);

        return Create(factory.Compile()(queryContext));
    }

    private DuckDBCommandPlan Create(object enumerable)
    {
        if (enumerable is not IRelationalQueryingEnumerable queryingEnumerable)
        {
            throw new NotSupportedException(
                "The query shape does not produce a single relational command. Client-only and multi-command queries are not supported.");
        }

        var enumerableType = enumerable.GetType();
        if (enumerableType.IsGenericType
            && enumerableType.GetGenericTypeDefinition() is var genericType
            && (genericType == typeof(SplitQueryingEnumerable<>)
                || genericType == typeof(GroupBySplitQueryingEnumerable<,>)))
        {
            throw new NotSupportedException(
                "Split queries produce multiple commands and cannot be represented by one DuckDBCommandPlan.");
        }

        using var metadata = DuckDBParameterMetadataRegistry.BeginCapture();
        using var command = queryingEnumerable.CreateDbCommand();
        if (!ReferenceEquals(command.Connection, currentDbContext.Context.Database.GetDbConnection()))
        {
            throw new ArgumentException(
                "The query must belong to the same DbContext as the DatabaseFacade used to create the command plan.");
        }

        return Snapshot(command, metadata);
    }

    private DuckDBCommandPlan Snapshot(
        DbCommand command,
        DuckDBParameterMetadataRegistry.CaptureScope metadata)
        => new(
            command.CommandText,
            command.Parameters.Cast<DbParameter>().Select(parameter =>
            {
                var value = parameter.Value is DBNull ? null : parameter.Value;
                metadata.TryGetTypeMapping(parameter, out var mapping);
                var snapshot = value is null
                    ? null
                    : mapping?.ProviderValueComparer.Snapshot(value) ?? value;

                return new DuckDBCommandPlanParameter(
                    parameter.ParameterName.RemoveDollarSign(),
                    mapping?.ClrType ?? value?.GetType() ?? typeof(object),
                    parameter.GetType(),
                    parameter.DbType,
                    parameter.IsNullable,
                    snapshot,
                    mapping?.StoreType,
                    mapping?.GetType().Name,
                    parameter.Direction == 0 ? System.Data.ParameterDirection.Input : parameter.Direction,
                    parameter.Size,
                    parameter.Precision,
                    parameter.Scale);
            }));

    private void ValidateQuery<T>(IQueryable<T> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!ReferenceEquals(query.Provider, queryProvider))
        {
            throw new ArgumentException(
                "The query must belong to the same DbContext as the DatabaseFacade used to create the command plan.",
                nameof(query));
        }
    }

    private sealed class QueryingEnumerableFinder : ExpressionVisitor
    {
        private Expression? _result;

        public Expression Find(Expression expression)
        {
            Visit(expression);
            return _result ?? throw new NotSupportedException(
                "The query shape does not produce a relational command that can be extracted.");
        }

        public override Expression? Visit(Expression? node)
        {
            if (_result is null
                && node is not null
                && typeof(IRelationalQueryingEnumerable).IsAssignableFrom(node.Type))
            {
                _result = node;
                return node;
            }

            return _result is null ? base.Visit(node) : node;
        }
    }
}