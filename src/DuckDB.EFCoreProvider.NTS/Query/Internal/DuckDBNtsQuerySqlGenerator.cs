using DuckDB.EFCoreProvider.NTS.Storage.Internal;
using DuckDB.EFCoreProvider.Query.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.NTS.Query.Internal;

/// <summary>
/// Extends <see cref="DuckDBQuerySqlGenerator"/> to handle DuckDB spatial geometry projections.
/// <para>
/// DuckDB.NET cannot read native GEOMETRY columns (type ID 40). When a geometry-typed expression
/// appears in a SELECT projection, this generator wraps it with <c>ST_AsWKT()</c> so that the
/// column is returned as a VARCHAR string that the driver can read.
/// The string is then parsed back to an NTS <see cref="NetTopologySuite.Geometries.Geometry"/>
/// instance by <see cref="DuckDBGeometryTypeMapping{TGeometry}"/>.
/// </para>
/// </summary>
public class DuckDBNtsQuerySqlGenerator : DuckDBQuerySqlGenerator
{
    /// <inheritdoc cref="DuckDBQuerySqlGenerator(QuerySqlGeneratorDependencies, bool)"/>
    public DuckDBNtsQuerySqlGenerator(
        QuerySqlGeneratorDependencies dependencies,
        bool reverseNullOrderingEnabled)
        : base(dependencies, reverseNullOrderingEnabled)
    {
    }

    /// <summary>
    /// If the projected expression has a DuckDB geometry type mapping, wraps it in
    /// <c>ST_AsWKT()</c> so that DuckDB returns a VARCHAR that the .NET driver can handle.
    /// Otherwise delegates to the base implementation.
    /// </summary>
    protected override Expression VisitProjection(ProjectionExpression projectionExpression)
    {
        var expr = projectionExpression.Expression;

        if (expr.TypeMapping is IDuckDBGeometryTypeMapping)
        {
            // Emit: ST_AsWKT(<expr>) AS "<alias>"
            Sql.Append("ST_AsWKT(");
            Visit(expr);
            Sql.Append(")");

            // Always emit the alias for an ST_AsWKT(...) call – the column name is not
            // preserved by the function so EF Core's reader always needs to use the alias.
            if (!string.IsNullOrEmpty(projectionExpression.Alias))
            {
                Sql.Append(AliasSeparator)
                   .Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(projectionExpression.Alias));
            }

            return projectionExpression;
        }

        return base.VisitProjection(projectionExpression);
    }
}

