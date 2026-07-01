using DuckDB.EFCoreProvider.NTS.Storage.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using NetTopologySuite.Geometries;

namespace DuckDB.EFCoreProvider.NTS.Query.Internal;

/// <summary>
/// Helper methods for DuckDB spatial SQL expression generation.
/// <para>
/// DuckDB stores geometry as native GEOMETRY columns. SQL parameters, however, carry WKT
/// text (a VARCHAR string), so they must be wrapped with <c>ST_GeomFromText()</c> before
/// being passed to a spatial function.
/// </para>
/// <para>
/// Column expressions and spatial-function results are already the DuckDB GEOMETRY type and
/// do not need any conversion. SQL constants already emit <c>ST_GeomFromText('...')</c>
/// via <c>GenerateNonNullSqlLiteral</c>.
/// </para>
/// </summary>
internal static class DuckDBSpatialHelpers
{
    /// <summary>
    /// Wraps a <see cref="SqlParameterExpression"/> with <c>ST_GeomFromText()</c> when its
    /// type mapping is a DuckDB geometry mapping (i.e., the parameter value is a WKT string).
    /// All other expression kinds are returned unchanged.
    /// </summary>
    public static SqlExpression AsGeometry(SqlExpression expression, ISqlExpressionFactory factory)
    {
        // Only parameters need wrapping – they carry WKT text.
        // • ColumnExpression        → native GEOMETRY column, already the right type
        // • SqlFunctionExpression   → spatial-function result, already GEOMETRY
        // • SqlConstantExpression   → GenerateNonNullSqlLiteral emits ST_GeomFromText('...')
        if (expression is SqlParameterExpression
            && expression.TypeMapping is IDuckDBGeometryTypeMapping)
        {
            return factory.Function(
                "ST_GeomFromText",
                [expression],
                nullable: true,
                argumentsPropagateNullability: [true],
                returnType: expression.Type,
                typeMapping: null); // null = raw DuckDB GEOMETRY (unmapped CLR side)
        }

        return expression;
    }
}
