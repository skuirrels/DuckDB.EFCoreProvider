using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using NetTopologySuite.Geometries;
using System.Reflection;

namespace DuckDB.EFCoreProvider.NTS.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
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