using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.ExpressionTranslators.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <remarks>Translates <c>Count</c> of a MAP-mapped dictionary to the number of entries.</remarks>
public class DuckDBMapMemberTranslator(ISqlExpressionFactory sqlExpressionFactory) : IMemberTranslator
{
    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        => instance is { TypeMapping: DuckDBMapTypeMapping }
            && member.Name == nameof(ICollection<int>.Count)
            && returnType == typeof(int)
                // cardinality() returns UBIGINT, so convert it to the CLR property's int.
                ? sqlExpressionFactory.Convert(
                    sqlExpressionFactory.Function(
                        "cardinality",
                        [instance],
                        nullable: true,
                        argumentsPropagateNullability: [true],
                        typeof(ulong)),
                    typeof(int))
                : null;
}