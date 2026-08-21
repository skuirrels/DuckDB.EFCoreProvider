using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Expressions.Internal;

/// <summary>
///     Semantic provenance for an EF-generated whole-complex null-presence check
///     (<c>entity.Complex == null</c> / <c>entity.Complex != null</c>).
/// </summary>
/// <remarks>
///     EF Core narrows a whole-complex null comparison to one or more representative leaf
///     columns (see <c>StructuralEquality.TryGenerateComparisons</c>). On DuckDB such per-leaf
///     checks are both slower than a struct-level check and invalid when the physical STRUCT is
///     sparse (the leaf key may not exist). This marker records the provenance - the operator
///     polarity and the nesting depth of the checked complex below its struct root - so a later
///     postprocessor can replace the narrowed comparison with a single
///     <c>struct_col IS NULL</c> / <c>struct_col IS NOT NULL</c> check. The marker is
///     SQL-transparent: rendering simply emits <see cref="CheckedExpression" />.
/// </remarks>
public sealed class DuckDBStructPresenceCheckExpression : SqlExpression, IEquatable<DuckDBStructPresenceCheckExpression>
{
    private static ConstructorInfo? _quotingConstructor;
    private readonly int _depth;
    private readonly string? _structColumnName;
    private readonly IReadOnlyList<string> _fieldPath;

    public DuckDBStructPresenceCheckExpression(
        ExpressionType operatorType,
        SqlExpression checkedExpression)
        : this(operatorType, checkedExpression, 0, null, [], typeof(bool), checkedExpression.TypeMapping)
    {
    }

    public DuckDBStructPresenceCheckExpression(
        ExpressionType operatorType,
        SqlExpression checkedExpression,
        int depth)
        : this(operatorType, checkedExpression, depth, null, [], typeof(bool), checkedExpression.TypeMapping)
    {
    }

    public DuckDBStructPresenceCheckExpression(
        ExpressionType operatorType,
        SqlExpression checkedExpression,
        int depth,
        string? structColumnName,
        IReadOnlyList<string> fieldPath)
        : this(operatorType, checkedExpression, depth, structColumnName, fieldPath, typeof(bool), checkedExpression.TypeMapping)
    {
    }

    public DuckDBStructPresenceCheckExpression(
        ExpressionType operatorType,
        SqlExpression checkedExpression,
        int depth,
        string? structColumnName,
        IReadOnlyList<string> fieldPath,
        Type type,
        RelationalTypeMapping? typeMapping)
        : base(type, typeMapping)
    {
        ArgumentNullException.ThrowIfNull(checkedExpression);
        ArgumentNullException.ThrowIfNull(fieldPath);
        if (operatorType is not (ExpressionType.Equal or ExpressionType.NotEqual))
        {
            throw new ArgumentException(
                "A struct presence check must be an Equal or NotEqual comparison.",
                nameof(operatorType));
        }

        if (depth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "The nesting depth must be non-negative.");
        }

        OperatorType = operatorType;
        CheckedExpression = checkedExpression;
        _depth = depth;
        _structColumnName = structColumnName;
        _fieldPath = fieldPath.ToArray();
    }

    /// <summary>
    ///     The polarity of the presence check. Equal means "the complex property is null" and
    ///     NotEqual means "the complex property is not null".
    /// </summary>
    public ExpressionType OperatorType { get; }

    /// <summary>The narrowed SQL comparison EF Core generated for the whole-complex null check.</summary>
    public SqlExpression CheckedExpression { get; }

    /// <summary>
    ///     The number of complex properties between the checked complex and its struct root.
    ///     Zero means the checked complex is the struct root itself.
    /// </summary>
    public int Depth => _depth;

    /// <summary>
    ///     The physical DuckDB STRUCT root column that owns the checked complex, taken from the
    ///     complex property's immutable struct mapping. <see langword="null" /> when the checked
    ///     complex could not be resolved to a struct-mapped complex property.
    /// </summary>
    public string? StructColumnName => _structColumnName;

    /// <summary>
    ///     The physical field path from the struct root to the checked complex. Empty when the
    ///     checked complex is the struct root itself.
    /// </summary>
    public IReadOnlyList<string> FieldPath => _fieldPath;

    public DuckDBStructPresenceCheckExpression Update(SqlExpression checkedExpression)
        => ReferenceEquals(checkedExpression, CheckedExpression)
            ? this
            : new DuckDBStructPresenceCheckExpression(
                OperatorType, checkedExpression, _depth, _structColumnName, _fieldPath, Type, TypeMapping);

    protected override Expression VisitChildren(ExpressionVisitor visitor)
        => Update((SqlExpression)visitor.Visit(CheckedExpression)!);

    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DuckDBStructPresenceCheckExpression).GetConstructor(
                [
                    typeof(ExpressionType),
                    typeof(SqlExpression),
                    typeof(int),
                    typeof(string),
                    typeof(IReadOnlyList<string>),
                    typeof(Type),
                    typeof(RelationalTypeMapping)
                ])!,
            Constant(OperatorType),
            CheckedExpression.Quote(),
            Constant(Depth),
            Constant(StructColumnName, typeof(string)),
            NewArrayInit(
                typeof(string),
                _fieldPath.Select(field => (Expression)Constant(field)).ToArray()),
            Constant(Type),
            RelationalExpressionQuotingUtilities.QuoteTypeMapping(TypeMapping));

    protected override void Print(ExpressionPrinter expressionPrinter)
        => expressionPrinter.Visit(CheckedExpression);

    public override string ToString()
        => $"StructPresenceCheck[{OperatorType}] {CheckedExpression}";

    public bool Equals(DuckDBStructPresenceCheckExpression? other)
        => other is not null
            && base.Equals(other)
            && OperatorType == other.OperatorType
            && CheckedExpression.Equals(other.CheckedExpression)
            && _depth == other._depth
            && string.Equals(_structColumnName, other._structColumnName, StringComparison.Ordinal)
            && _fieldPath.SequenceEqual(other._fieldPath, StringComparer.Ordinal);

    public override bool Equals(object? obj)
        => Equals(obj as DuckDBStructPresenceCheckExpression);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(base.GetHashCode());
        hash.Add(OperatorType);
        hash.Add(CheckedExpression);
        hash.Add(Depth);
        hash.Add(_structColumnName, StringComparer.Ordinal);
        foreach (var field in _fieldPath)
        {
            hash.Add(field, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
