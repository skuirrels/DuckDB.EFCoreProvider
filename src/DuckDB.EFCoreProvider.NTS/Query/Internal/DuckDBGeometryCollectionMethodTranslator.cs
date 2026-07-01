using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using NetTopologySuite.Geometries;
using System.Reflection;

namespace DuckDB.EFCoreProvider.NTS.Query.Internal;

public class DuckDBGeometryCollectionMethodTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo ElementAtMethod =
        typeof(Enumerable).GetRuntimeMethods()
            .First(m => m.Name == nameof(Enumerable.ElementAt)
                        && m.GetParameters() is [_, { ParameterType: var pt }]
                        && pt == typeof(int));

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public DuckDBGeometryCollectionMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
        => _sqlExpressionFactory = sqlExpressionFactory;

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method.IsGenericMethod
            && method.GetGenericMethodDefinition() == ElementAtMethod
            && method.ReturnType == typeof(Geometry)
            && arguments is [var collection, var index])
        {
            return _sqlExpressionFactory.Function(
                "ST_CollectionExtract",
                [DuckDBSpatialHelpers.AsGeometry(collection, _sqlExpressionFactory), _sqlExpressionFactory.Add(index, _sqlExpressionFactory.Constant(1))],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                method.ReturnType);
        }

        return null;
    }
}