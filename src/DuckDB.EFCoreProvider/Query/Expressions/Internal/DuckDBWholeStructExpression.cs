using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Expressions.Internal;

/// <summary>
///     A DuckDB expression that reads an entire STRUCT column as one value instead of
///     extracting a single physical field with <c>struct."field"</c>. The extraction of
///     individual materialized fields is driven client-side by the attached
///     <see cref="DuckDB.EFCoreProvider.Storage.Internal.DuckDBStructKeyTypeMapping"/>.
/// </summary>
/// <remarks>
///     This node is only introduced for whole-struct (complex-type root) projections. It must
///     not be used for single-field projections, which keep the field-extraction SQL shape.
/// </remarks>
public sealed class DuckDBWholeStructExpression : SqlExpression, IEquatable<DuckDBWholeStructExpression>
{
    private static ConstructorInfo? _quotingConstructor;
    private readonly ImmutableArray<string> _fieldPath;

    public DuckDBWholeStructExpression(
        SqlExpression source,
        IReadOnlyList<string> fieldPath,
        RelationalTypeMapping? typeMapping = null)
        : base(typeof(object), typeMapping)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (fieldPath.Count == 0 || fieldPath.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A STRUCT field path must contain non-empty names.", nameof(fieldPath));
        }

        Source = source;
        _fieldPath = fieldPath.ToImmutableArray();
    }

    /// <summary>The resolved STRUCT source expression, normally a physical root column.</summary>
    public SqlExpression Source { get; }

    /// <summary>
    ///     The physical path of the field this projection entry materializes. Empty for the
    ///     struct root slot, in which case the whole dictionary is read as-is.
    /// </summary>
    public IReadOnlyList<string> FieldPath => _fieldPath;

    public DuckDBWholeStructExpression Update(SqlExpression source)
        => ReferenceEquals(source, Source)
            ? this
            : new DuckDBWholeStructExpression(source, _fieldPath, TypeMapping);

    protected override Expression VisitChildren(ExpressionVisitor visitor)
        => Update((SqlExpression)visitor.Visit(Source)!);

    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DuckDBWholeStructExpression).GetConstructor(
                [
                    typeof(SqlExpression),
                    typeof(string[]),
                    typeof(RelationalTypeMapping)
                ])!,
            Source.Quote(),
            NewArrayInit(
                typeof(string),
                _fieldPath.Select(field => (Expression)Constant(field)).ToArray()),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));

    protected override void Print(ExpressionPrinter expressionPrinter)
        => expressionPrinter.Visit(Source);

    public override string ToString()
    {
        try
        {
            return Source.ToString() ?? Source.GetType().Name;
        }
        catch
        {
            return GetType().Name;
        }
    }

    public bool Equals(DuckDBWholeStructExpression? other)
        => other is not null
            && base.Equals(other)
            && Source.Equals(other.Source)
            && _fieldPath.SequenceEqual(other._fieldPath, StringComparer.Ordinal);

    public override bool Equals(object? obj)
        => Equals(obj as DuckDBWholeStructExpression);

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
