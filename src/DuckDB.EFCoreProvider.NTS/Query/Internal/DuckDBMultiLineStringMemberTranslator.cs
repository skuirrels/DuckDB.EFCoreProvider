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
public class DuckDBMultiLineStringMemberTranslator : IMemberTranslator
{
    private static readonly MemberInfo IsClosed
        = typeof(MultiLineString).GetTypeInfo().GetRuntimeProperty(nameof(MultiLineString.IsClosed))!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public DuckDBMultiLineStringMemberTranslator(ISqlExpressionFactory sqlExpressionFactory)
        => _sqlExpressionFactory = sqlExpressionFactory;

    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        => Equals(member, IsClosed)
            ? _sqlExpressionFactory.Case(
                [
                    new CaseWhenClause(
                        _sqlExpressionFactory.IsNotNull(instance!),
                        _sqlExpressionFactory.Function(
                            "ST_IsClosed",
                            [DuckDBSpatialHelpers.AsGeometry(instance!, _sqlExpressionFactory)],
                            nullable: false,
                            argumentsPropagateNullability: [false],
                            returnType))
                ],
                null)
            : null;
}

