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
public class DuckDBGeometryMethodTranslator : IMethodCallTranslator
{
    // Methods that map directly to ST_* function with same arguments (instance becomes first arg)
    private static readonly IDictionary<MethodInfo, string> MethodToFunctionName = new Dictionary<MethodInfo, string>
    {
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.AsBinary), Type.EmptyTypes)!, "ST_AsBinary" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.AsText), Type.EmptyTypes)!, "ST_AsText" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Buffer), [typeof(double)])!, "ST_Buffer" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Contains), [typeof(Geometry)])!, "ST_Contains" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.ConvexHull), Type.EmptyTypes)!, "ST_ConvexHull" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.CoveredBy), [typeof(Geometry)])!, "ST_CoveredBy" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Covers), [typeof(Geometry)])!, "ST_Covers" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Crosses), [typeof(Geometry)])!, "ST_Crosses" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Difference), [typeof(Geometry)])!, "ST_Difference" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Disjoint), [typeof(Geometry)])!, "ST_Disjoint" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Distance), [typeof(Geometry)])!, "ST_Distance" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.EqualsTopologically), [typeof(Geometry)])!, "ST_Equals" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Intersection), [typeof(Geometry)])!, "ST_Intersection" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Intersects), [typeof(Geometry)])!, "ST_Intersects" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Normalized), Type.EmptyTypes)!, "ST_Normalize" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Overlaps), [typeof(Geometry)])!, "ST_Overlaps" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Reverse), Type.EmptyTypes)!, "ST_Reverse" },
        // SymmetricDifference is handled separately below (no native ST_SymDifference in DuckDB)
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.ToBinary), Type.EmptyTypes)!, "ST_AsBinary" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.ToText), Type.EmptyTypes)!, "ST_AsText" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Touches), [typeof(Geometry)])!, "ST_Touches" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Union), Type.EmptyTypes)!, "ST_Union" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Union), [typeof(Geometry)])!, "ST_Union" },
        { typeof(Geometry).GetRuntimeMethod(nameof(Geometry.Within), [typeof(Geometry)])!, "ST_Within" }
    };

    private static readonly MethodInfo SymmetricDifferenceMethod
        = typeof(Geometry).GetRuntimeMethod(nameof(Geometry.SymmetricDifference), [typeof(Geometry)])!;

    private static readonly MethodInfo GetGeometryN
        = typeof(Geometry).GetRuntimeMethod(nameof(Geometry.GetGeometryN), [typeof(int)])!;

    private static readonly MethodInfo IsWithinDistance
        = typeof(Geometry).GetRuntimeMethod(nameof(Geometry.IsWithinDistance), [typeof(Geometry), typeof(double)])!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public DuckDBGeometryMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
        => _sqlExpressionFactory = sqlExpressionFactory;

    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance == null)
        {
            return null;
        }

        if (MethodToFunctionName.TryGetValue(method, out var functionName))
        {
            // Wrap the instance and any geometry arguments with ST_GeomFromWKB if they are BLOB columns/params
            var wrappedInstance = DuckDBSpatialHelpers.AsGeometry(instance, _sqlExpressionFactory);
            var wrappedArgs = arguments.Select(a => DuckDBSpatialHelpers.AsGeometry(a, _sqlExpressionFactory)).ToArray();
            var allArgs = new[] { wrappedInstance }.Concat(wrappedArgs);

            if (method.ReturnType == typeof(bool))
            {
                var nullCheck = (SqlExpression)_sqlExpressionFactory.IsNotNull(instance);
                foreach (var argument in arguments)
                {
                    nullCheck = _sqlExpressionFactory.AndAlso(nullCheck, _sqlExpressionFactory.IsNotNull(argument));
                }

                return _sqlExpressionFactory.Case(
                    [
                        new CaseWhenClause(
                            nullCheck,
                            _sqlExpressionFactory.Function(
                                functionName,
                                allArgs,
                                nullable: false,
                                allArgs.Select(_ => false),
                                method.ReturnType))
                    ],
                    null);
            }

            return _sqlExpressionFactory.Function(
                functionName,
                allArgs,
                nullable: true,
                allArgs.Select(_ => true),
                method.ReturnType);
        }

        if (Equals(method, GetGeometryN))
        {
            return _sqlExpressionFactory.Function(
                "ST_GeometryN",
                [DuckDBSpatialHelpers.AsGeometry(instance, _sqlExpressionFactory), _sqlExpressionFactory.Add(arguments[0], _sqlExpressionFactory.Constant(1))],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                method.ReturnType);
        }

        if (Equals(method, IsWithinDistance))
        {
            return _sqlExpressionFactory.Function(
                "ST_DWithin",
                [DuckDBSpatialHelpers.AsGeometry(instance, _sqlExpressionFactory), DuckDBSpatialHelpers.AsGeometry(arguments[0], _sqlExpressionFactory), arguments[1]],
                nullable: true,
                argumentsPropagateNullability: [true, true, true],
                typeof(bool));
        }

        // DuckDB has no ST_SymDifference; emulate as ST_Union(ST_Difference(A,B), ST_Difference(B,A))
        if (Equals(method, SymmetricDifferenceMethod))
        {
            var a = DuckDBSpatialHelpers.AsGeometry(instance, _sqlExpressionFactory);
            var b = DuckDBSpatialHelpers.AsGeometry(arguments[0], _sqlExpressionFactory);

            var diffAB = _sqlExpressionFactory.Function(
                "ST_Difference", [a, b],
                nullable: true, argumentsPropagateNullability: [true, true],
                method.ReturnType);

            var diffBA = _sqlExpressionFactory.Function(
                "ST_Difference", [b, a],
                nullable: true, argumentsPropagateNullability: [true, true],
                method.ReturnType);

            return _sqlExpressionFactory.Function(
                "ST_Union", [diffAB, diffBA],
                nullable: true, argumentsPropagateNullability: [true, true],
                method.ReturnType);
        }

        return null;
    }
}
