using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Expressions.Internal;

/// <summary>
///     A SQL expression that renders as DuckDB STRUCT field access syntax:
///     <c>alias."StructColumn".nested1.nested2.leaf</c>.
/// </summary>
/// <remarks>
///     Internally this carries the table alias, the physical STRUCT column name, the
///     intermediate nested field names, and the leaf field name. Rendering is handled by
///     <see cref="Query.Internal.DuckDBQuerySqlGenerator.VisitStructField" />. The expression
///     is shaped from a <c>DuckDB:StructField</c> annotation
///     (<seealso cref="DuckDBStructFieldInfo" />) carried by the underlying property's
///     column mapping.
/// </remarks>
public sealed class DuckDBStructFieldExpression : SqlExpression, IEquatable<DuckDBStructFieldExpression>
{
    private static ConstructorInfo? _quotingConstructor;

    /// <summary>
    ///     Creates a STRUCT field access expression.
    /// </summary>
    /// <param name="tableAlias">The SQL table alias the STRUCT column lives on (e.g. <c>t</c>, <c>Customers</c>).</param>
    /// <param name="structColumnName">The physical DuckDB STRUCT column name (e.g. <c>Location</c>).</param>
    /// <param name="structFieldInfo">
    ///     The struct field metadata carrying nested and leaf field names. The caller's
    ///     arrays are NOT defensively copied here — <see cref="DuckDBStructFieldInfo" /> is
    ///     an immutable <c>record</c> with a defensive copy taken in its constructor.
    /// </param>
    /// <param name="type">The CLR type of the leaf field's projected value.</param>
    /// <param name="typeMapping">Optional type mapping for the leaf value.</param>
    public DuckDBStructFieldExpression(
        string tableAlias,
        string structColumnName,
        DuckDBStructFieldInfo structFieldInfo,
        Type type,
        RelationalTypeMapping? typeMapping = null)
        : base(type, typeMapping)
    {
        ArgumentNullException.ThrowIfNull(structFieldInfo);
        if (string.IsNullOrEmpty(tableAlias))
        {
            throw new ArgumentException("tableAlias must be non-empty", nameof(tableAlias));
        }

        if (string.IsNullOrEmpty(structColumnName))
        {
            throw new ArgumentException("structColumnName must be non-empty", nameof(structColumnName));
        }

        TableAlias = tableAlias;
        StructColumnName = structColumnName;
        StructFieldInfo = structFieldInfo;
    }

    /// <summary>The SQL table alias the STRUCT column lives on (e.g. <c>t</c>).</summary>
    public string TableAlias { get; }

    /// <summary>The physical DuckDB STRUCT column name (e.g. <c>Location</c>).</summary>
    public string StructColumnName { get; }

    /// <summary>The struct field metadata carrying nested and leaf field names.</summary>
    public DuckDBStructFieldInfo StructFieldInfo { get; }

    /// <inheritdoc />
    protected override Expression VisitChildren(ExpressionVisitor visitor)
        => this;

    /// <summary>
    ///     Creates a new expression that is like this one, but using the supplied children.
    ///     If all of the children are the same, returns this instance.
    /// </summary>
    public DuckDBStructFieldExpression Update(
        string tableAlias,
        string structColumnName,
        DuckDBStructFieldInfo structFieldInfo)
        => tableAlias == TableAlias
           && structColumnName == StructColumnName
           && structFieldInfo == StructFieldInfo
            ? this
            : new DuckDBStructFieldExpression(tableAlias, structColumnName, structFieldInfo, Type, TypeMapping);

    /// <inheritdoc />
    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DuckDBStructFieldExpression).GetConstructor(
                [typeof(string), typeof(string), typeof(DuckDBStructFieldInfo), typeof(Type), typeof(RelationalTypeMapping)])!,
            Constant(TableAlias),
            Constant(StructColumnName),
                MemberInit(
                    New(typeof(DuckDBStructFieldInfo).GetConstructor(
                        [typeof(string), typeof(string[]), typeof(string)])!),
                    Bind(typeof(DuckDBStructFieldInfo).GetProperty(nameof(DuckDBStructFieldInfo.StructColumnName))!,
                         Constant(StructFieldInfo.StructColumnName)),
                    Bind(typeof(DuckDBStructFieldInfo).GetProperty(nameof(DuckDBStructFieldInfo.NestedFieldNames))!,
                         NewArrayInit(typeof(string),
                             StructFieldInfo.NestedFieldNames.Select(f => (Expression)Constant(f)).ToArray())),
                    Bind(typeof(DuckDBStructFieldInfo).GetProperty(nameof(DuckDBStructFieldInfo.LeafFieldName))!,
                         StructFieldInfo.LeafFieldName is null
                             ? (Expression)Constant(null, typeof(string))
                             : Constant(StructFieldInfo.LeafFieldName))),
                Constant(Type),
                RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));

    /// <inheritdoc />
    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter
                .Append("\"").Append(TableAlias).Append("\"")
                .Append(".\"").Append(StructColumnName).Append("\"");

        foreach (var field in StructFieldInfo.NestedFieldNames)
        {
            expressionPrinter.Append(".").Append(field);
        }

        expressionPrinter
            .Append(".").Append(StructFieldInfo.LeafFieldName ?? "<column>");
    }

        /// <inheritdoc />
        public override string ToString()
            => $"\"{TableAlias}\".\"{StructColumnName}\""
               + (StructFieldInfo.NestedFieldNames.Count == 0
                    ? string.Empty
                    : "." + string.Join(".", StructFieldInfo.NestedFieldNames))
               + "." + (StructFieldInfo.LeafFieldName ?? "<column>");

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is DuckDBStructFieldExpression other && Equals(other);

    /// <inheritdoc />
    public bool Equals(DuckDBStructFieldExpression? other)
        => other is not null
           && base.Equals(other)
           && other.TableAlias == TableAlias
           && other.StructColumnName == StructColumnName
           && other.StructFieldInfo == StructFieldInfo;

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(base.GetHashCode());
        hashCode.Add(TableAlias, StringComparer.Ordinal);
        hashCode.Add(StructColumnName, StringComparer.Ordinal);
        hashCode.Add(StructFieldInfo);
        return hashCode.ToHashCode();
    }
}