using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using NetTopologySuite.Geometries;
using System.Reflection;

namespace DuckDB.EFCoreProvider.NTS.Query.Internal;

public class DuckDBLineStringMethodTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo GetPointN
        = typeof(LineString).GetRuntimeMethod(nameof(LineString.GetPointN), [typeof(int)])!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public DuckDBLineStringMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
        => _sqlExpressionFactory = sqlExpressionFactory;

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (!Equals(method, GetPointN))
        {
            return null;
        }

        return _sqlExpressionFactory.Function(
            "ST_PointN",
            [DuckDBSpatialHelpers.AsGeometry(instance!, _sqlExpressionFactory), _sqlExpressionFactory.Add(arguments[0], _sqlExpressionFactory.Constant(1))],
            nullable: true,
            argumentsPropagateNullability: [true, true],
            method.ReturnType);
    }
}