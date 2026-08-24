using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Expressions.Internal;

/// <summary>
///     A typed DuckDB STRUCT field access whose source and physical path were resolved
///     before SQL generation.
/// </summary>
public sealed class DuckDBStructFieldExpression : SqlExpression, IEquatable<DuckDBStructFieldExpression>
{
    private static ConstructorInfo? _quotingConstructor;
    private readonly ImmutableArray<string> _fieldPath;

    public DuckDBStructFieldExpression(
        SqlExpression source,
        IReadOnlyList<string> fieldPath,
        Type type,
        RelationalTypeMapping? typeMapping = null)
        : this(source, fieldPath.ToArray(), type, typeMapping)
    {
    }

    private DuckDBStructFieldExpression(
        SqlExpression source,
        string[] fieldPath,
        Type type,
        RelationalTypeMapping? typeMapping)
        : base(type, typeMapping)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (fieldPath.Length == 0 || fieldPath.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A STRUCT field path must contain non-empty names.", nameof(fieldPath));
        }

        Source = source;
        _fieldPath = fieldPath.ToImmutableArray();
    }

    /// <summary>
    ///     Compatibility constructor for callers that create a resolved node directly.
    /// </summary>
    public DuckDBStructFieldExpression(
        string tableAlias,
        string structColumnName,
        DuckDBStructFieldInfo structFieldInfo,
        Type type,
        RelationalTypeMapping? typeMapping = null)
        : this(
            new ColumnExpression(
                structColumnName,
                tableAlias,
                typeof(object),
                typeMapping: null,
                nullable: true),
            structFieldInfo.FieldPath,
            type,
            typeMapping)
    {
        ArgumentException.ThrowIfNullOrEmpty(tableAlias);
        ArgumentException.ThrowIfNullOrEmpty(structColumnName);
        ArgumentNullException.ThrowIfNull(structFieldInfo);
    }

    /// <summary>The resolved STRUCT source expression, normally a physical root column.</summary>
    public SqlExpression Source { get; }

    /// <summary>The immutable physical path from the source STRUCT to the leaf.</summary>
    public IReadOnlyList<string> FieldPath => _fieldPath;

    /// <summary>Compatibility view of the source table alias.</summary>
    public string TableAlias => (Source as ColumnExpression)?.TableAlias ?? string.Empty;

    /// <summary>Compatibility view of the physical root column name.</summary>
    public string StructColumnName => (Source as ColumnExpression)?.Name ?? string.Empty;

    /// <summary>Compatibility view of the resolved physical field metadata.</summary>
    public DuckDBStructFieldInfo StructFieldInfo
        => new(
            StructColumnName,
            _fieldPath.Length == 1 ? [] : _fieldPath[..^1].ToArray(),
            _fieldPath[^1]);

    public DuckDBStructFieldExpression Update(
        SqlExpression source,
        IReadOnlyList<string> fieldPath)
    {
        var immutablePath = fieldPath.ToImmutableArray();
        return ReferenceEquals(source, Source)
                && immutablePath.SequenceEqual(_fieldPath, StringComparer.Ordinal)
            ? this
            : new DuckDBStructFieldExpression(source, immutablePath, Type, TypeMapping);
    }

    /// <summary>Compatibility overload for the former alias-based expression shape.</summary>
    public DuckDBStructFieldExpression Update(
        string tableAlias,
        string structColumnName,
        DuckDBStructFieldInfo structFieldInfo)
        => new(tableAlias, structColumnName, structFieldInfo, Type, TypeMapping);

    protected override Expression VisitChildren(ExpressionVisitor visitor)
        => Update((SqlExpression)visitor.Visit(Source)!, _fieldPath);

    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DuckDBStructFieldExpression).GetConstructor(
                [
                    typeof(SqlExpression),
                    typeof(IReadOnlyList<string>),
                    typeof(Type),
                    typeof(RelationalTypeMapping)
                ])!,
            Source.Quote(),
            NewArrayInit(
                typeof(string),
                _fieldPath.Select(field => (Expression)Constant(field)).ToArray()),
            Constant(Type),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));

    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Visit(Source);
        foreach (var field in _fieldPath)
        {
            expressionPrinter.Append(".").Append(field);
        }
    }

    public override string ToString()
        => $"{Source}.{string.Join(".", _fieldPath)}";

    public bool Equals(DuckDBStructFieldExpression? other)
        => other is not null
            && base.Equals(other)
            && Source.Equals(other.Source)
            && _fieldPath.SequenceEqual(other._fieldPath, StringComparer.Ordinal);

    public override bool Equals(object? obj)
        => Equals(obj as DuckDBStructFieldExpression);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(base.GetHashCode());
        hash.Add(Source);
        foreach (var field in _fieldPath)
        {
            hash.Add(field, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}