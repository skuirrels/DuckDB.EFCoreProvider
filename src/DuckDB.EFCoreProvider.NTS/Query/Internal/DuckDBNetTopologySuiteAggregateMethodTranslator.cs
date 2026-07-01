using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Union;
using System.Reflection;

namespace DuckDB.EFCoreProvider.NTS.Query.Internal;

public class DuckDBNetTopologySuiteAggregateMethodTranslator : IAggregateMethodCallTranslator
{
    private static readonly MethodInfo GeometryCombineMethod
        = typeof(GeometryCombiner).GetRuntimeMethod(nameof(GeometryCombiner.Combine), [typeof(IEnumerable<Geometry>)])!;

    private static readonly MethodInfo ConvexHullMethod
        = typeof(ConvexHull).GetRuntimeMethod(nameof(ConvexHull.Create), [typeof(IEnumerable<Geometry>)])!;

    private static readonly MethodInfo UnionMethod
        = typeof(UnaryUnionOp).GetRuntimeMethod(nameof(UnaryUnionOp.Union), [typeof(IEnumerable<Geometry>)])!;

    private static readonly MethodInfo EnvelopeCombineMethod
        = typeof(EnvelopeCombiner).GetRuntimeMethod(nameof(EnvelopeCombiner.CombineAsGeometry), [typeof(IEnumerable<Geometry>)])!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public DuckDBNetTopologySuiteAggregateMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
        => _sqlExpressionFactory = sqlExpressionFactory;

    public SqlExpression? Translate(
        MethodInfo method,
        EnumerableExpression source,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (source.Selector is not SqlExpression sqlExpression)
        {
            return null;
        }

        // DuckDB aggregates:
        //   GeometryCombiner.Combine  → ST_Collect(list(geom))
        //   ConvexHull.Create         → ST_ConvexHull(ST_Collect(list(geom)))
        //   UnaryUnionOp.Union        → ST_Union_Agg(geom)   (native DuckDB aggregate)
        //   EnvelopeCombiner          → ST_Envelope_Agg(geom) (native DuckDB aggregate)

        if (method == GeometryCombineMethod)
        {
            return _sqlExpressionFactory.Function(
                "ST_Collect",
                [MakeListAggregate(sqlExpression, source)],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(Geometry));
        }

        if (method == ConvexHullMethod)
        {
            return _sqlExpressionFactory.Function(
                "ST_ConvexHull",
                [
                    _sqlExpressionFactory.Function(
                        "ST_Collect",
                        [MakeListAggregate(sqlExpression, source)],
                        nullable: true,
                        argumentsPropagateNullability: [true],
                        typeof(Geometry))
                ],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(Geometry));
        }

        if (method == UnionMethod)
        {
            return _sqlExpressionFactory.Function(
                "ST_Union_Agg",
                [ApplyPredicateAndDistinct(ref sqlExpression, source)],
                nullable: true,
                argumentsPropagateNullability: [false],
                typeof(Geometry));
        }

        if (method == EnvelopeCombineMethod)
        {
            return _sqlExpressionFactory.Function(
                "ST_Envelope_Agg",
                [ApplyPredicateAndDistinct(ref sqlExpression, source)],
                nullable: true,
                argumentsPropagateNullability: [false],
                typeof(Geometry));
        }

        return null;
    }

    /// <summary>
    /// Applies predicate / distinct to <paramref name="expr"/> and wraps it with
    /// DuckDB's <c>list()</c> aggregate so that the result is a <c>GEOMETRY[]</c>
    /// array suitable for passing to <c>ST_Collect</c>.
    /// </summary>
    private SqlExpression MakeListAggregate(SqlExpression expr, EnumerableExpression source)
    {
        ApplyPredicateAndDistinct(ref expr, source);
        var wrappedGeom = DuckDBSpatialHelpers.AsGeometry(expr, _sqlExpressionFactory);
        // Use the geometry type mapping so the postprocessor sees a non-null mapping.
        // list() is never materialized directly — it is always consumed by ST_Collect.
        return _sqlExpressionFactory.Function(
            "list",
            [wrappedGeom],
            nullable: true,
            argumentsPropagateNullability: [false],
            wrappedGeom.Type,
            wrappedGeom.TypeMapping); // GEOMETRY[] — borrow the geometry mapping as placeholder
    }

    /// <summary>Modifies <paramref name="expr"/> in-place for predicate/distinct, returns it.</summary>
    private SqlExpression ApplyPredicateAndDistinct(ref SqlExpression expr, EnumerableExpression source)
    {
        if (source.Predicate != null)
        {
            expr = _sqlExpressionFactory.Case(
                new List<CaseWhenClause> { new(source.Predicate, expr) },
                elseResult: null);
        }

        if (source.IsDistinct)
        {
            expr = new DistinctExpression(expr);
        }

        return DuckDBSpatialHelpers.AsGeometry(expr, _sqlExpressionFactory);
    }
}