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
public class DuckDBGeometryCollectionMemberTranslator : IMemberTranslator
{
    private static readonly MemberInfo Count
        = typeof(GeometryCollection).GetTypeInfo().GetRuntimeProperty(nameof(GeometryCollection.Count))!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public DuckDBGeometryCollectionMemberTranslator(ISqlExpressionFactory sqlExpressionFactory)
        => _sqlExpressionFactory = sqlExpressionFactory;

    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        => Equals(member, Count)
            ? _sqlExpressionFactory.Function(
                "ST_NumGeometries",
                [DuckDBSpatialHelpers.AsGeometry(instance!, _sqlExpressionFactory)],
                nullable: true,
                argumentsPropagateNullability: [true],
                returnType)
            : null;
}