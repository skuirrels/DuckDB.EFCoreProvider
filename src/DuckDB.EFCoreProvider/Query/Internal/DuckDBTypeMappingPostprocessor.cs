using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Diagnostics;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBTypeMappingPostprocessor : RelationalTypeMappingPostprocessor
{
    private readonly IModel _model;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public DuckDBTypeMappingPostprocessor(
        QueryTranslationPostprocessorDependencies dependencies,
        RelationalQueryTranslationPostprocessorDependencies relationalDependencies,
        RelationalQueryCompilationContext queryCompilationContext)
        : base(dependencies, relationalDependencies, queryCompilationContext)
    {
        _model = queryCompilationContext.Model;
        _typeMappingSource = relationalDependencies.TypeMappingSource;
        _sqlExpressionFactory = relationalDependencies.SqlExpressionFactory;
    }

    /// <inheritdoc />
    protected override Expression VisitExtension(Expression expression)
    {
        switch (expression)
        {
            case DuckDBJsonEachExpression jsonEachExpression
                when TryGetInferredTypeMapping(
                    jsonEachExpression.Alias,
                    DuckDBQueryableMethodTranslatingExpressionVisitor.JsonEachValueColumnName,
                    out var typeMapping):
                return ApplyTypeMappingsOnJsonEachExpression(jsonEachExpression, typeMapping);

            case DuckDBUnnestExpression unnestExpression
                when TryGetInferredTypeMapping(unnestExpression.Alias, unnestExpression.ColumnName, out var elementTypeMapping):
                {
                    var collectionTypeMapping = RelationalDependencies.TypeMappingSource.FindMapping(unnestExpression.Array.Type, _model, elementTypeMapping);

                    if (collectionTypeMapping is null)
                    {
                        throw new InvalidOperationException(RelationalStrings.NullTypeMappingInSqlTree(expression.Print()));
                    }

                    return unnestExpression.Update(
                        _sqlExpressionFactory.ApplyTypeMapping(unnestExpression.Array, collectionTypeMapping));
                }

            default:
                return base.VisitExtension(expression);
        }
    }

    protected virtual DuckDBJsonEachExpression ApplyTypeMappingsOnJsonEachExpression(
        DuckDBJsonEachExpression jsonEachExpression,
        RelationalTypeMapping inferredTypeMapping)
    {
        if (jsonEachExpression.Arguments[0] is not SqlParameterExpression parameterExpression)
        {
            return jsonEachExpression;
        }

        if (_typeMappingSource.FindMapping(parameterExpression.Type, _model, inferredTypeMapping) is not DuckDBStringTypeMapping
            parameterTypeMapping)
        {
            throw new InvalidOperationException("Type mapping for 'string' could not be found or was not a DuckDBStringTypeMapping");
        }

        Debug.Assert(parameterTypeMapping.ElementTypeMapping != null, "Collection type mapping missing element mapping.");

        return jsonEachExpression.Update(
            parameterExpression.ApplyTypeMapping(parameterTypeMapping),
            jsonEachExpression.Path);
    }
}
