using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Expressions.Internal;

/// <summary>
///     An expression that represents a DuckDB <c>unnest</c> function call in a SQL tree.
/// </summary>
/// <remarks>
///     When <see cref="WithOrdinality" /> is <see langword="true" />, the expression is translated to a subquery that includes
///     both the unnested values and their 1-based subscripts via <c>generate_subscripts(array, 1) AS ordinality</c>,
///     since DuckDB does not support the <c>WITH ORDINALITY</c> clause.
/// </remarks>
public class DuckDBUnnestExpression : TableValuedFunctionExpression, IEquatable<DuckDBUnnestExpression>
{
    /// <summary>
    ///     The array to be un-nested into a table.
    /// </summary>
    public virtual SqlExpression Array => Arguments[0];

    /// <summary>
    ///     Column information for the output of the <c>unnest</c> call.
    /// </summary>
    public virtual IReadOnlyList<ColumnInfo>? ColumnInfos { get; }

    /// <summary>
    ///     The name of the column to be projected out from the <c>unnest</c> call.
    /// </summary>
    public virtual string ColumnName => ColumnInfos![0].Name;

    /// <summary>
    ///     Whether to project an additional ordinality column containing the 1-based index of each element.
    ///     When <see langword="true" />, a <c>generate_subscripts</c> call is emitted alongside <c>unnest</c>.
    /// </summary>
    public virtual bool WithOrdinality { get; }

    /// <summary>
    ///     Creates a new <see cref="DuckDBUnnestExpression" />.
    /// </summary>
    public DuckDBUnnestExpression(
        string alias,
        SqlExpression array,
        string columnName,
        bool withOrdinality = true)
        : this(alias, array, new ColumnInfo(columnName), annotations: null, withOrdinality)
    {
    }

    private DuckDBUnnestExpression(
        string alias,
        SqlExpression array,
        ColumnInfo? columnInfo,
        IReadOnlyDictionary<string, IAnnotation>? annotations,
        bool withOrdinality = true)
        : base(alias, "unnest", schema: null, builtIn: true, [array], annotations)
    {
        ColumnInfos = columnInfo is null ? null : [columnInfo.Value];
        WithOrdinality = withOrdinality;
    }

    /// <inheritdoc />
    protected override Expression VisitChildren(ExpressionVisitor visitor)
        => visitor.Visit(Array) is var visitedArray && visitedArray == Array
            ? this
            : Update((SqlExpression)visitedArray);

    /// <inheritdoc />
    public override TableValuedFunctionExpression Update(IReadOnlyList<SqlExpression> arguments)
        => arguments is [var singleArgument]
            ? Update(singleArgument)
            : throw new ArgumentException(
                $"An unnest expression takes exactly one argument (the array), but {arguments.Count} were supplied.",
                nameof(arguments));

    /// <summary>
    ///     Creates a new expression that is like this one, but using the supplied array. If the array is the same,
    ///     it will return this expression.
    /// </summary>
    public virtual DuckDBUnnestExpression Update(SqlExpression array)
        => array == Array
            ? this
            : new DuckDBUnnestExpression(
                Alias,
                array,
                GetSingleColumnInfo(),
                Annotations,
                WithOrdinality);

    /// <inheritdoc />
    public override TableExpressionBase Clone(string? alias, ExpressionVisitor cloningExpressionVisitor)
        => new DuckDBUnnestExpression(
            alias ?? Alias,
            (SqlExpression)cloningExpressionVisitor.Visit(Array),
            GetSingleColumnInfo(),
            Annotations,
            WithOrdinality);

    /// <inheritdoc />
    public override TableValuedFunctionExpression WithAlias(string newAlias)
        => new DuckDBUnnestExpression(
            newAlias,
            Array,
            GetSingleColumnInfo(),
            Annotations,
            WithOrdinality);

    /// <summary>
    ///     Returns a new expression with the given column infos applied.
    /// </summary>
    public virtual DuckDBUnnestExpression WithColumnInfos(IReadOnlyList<ColumnInfo>? columnInfos)
        => new DuckDBUnnestExpression(
            Alias,
            Array,
            columnInfos switch
            {
                null or [] => null,
                [var columnInfo] => columnInfo,
                _ => throw new ArgumentException(
                    $"An unnest expression produces exactly one output column, but {columnInfos.Count} column infos were supplied.",
                    nameof(columnInfos))
            },
            Annotations,
            WithOrdinality);

    /// <inheritdoc />
    protected override TableValuedFunctionExpression WithAnnotations(IReadOnlyDictionary<string, IAnnotation> annotations)
        => new DuckDBUnnestExpression(Alias, Array, GetSingleColumnInfo(), annotations, WithOrdinality);

    /// <inheritdoc />
    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Append("unnest(");
        expressionPrinter.Visit(Array);
        expressionPrinter.Append(")");

        if (WithOrdinality)
        {
            expressionPrinter.Append(" [WITH generate_subscripts]");
        }

        expressionPrinter.Append(" AS ").Append(Alias);

        if (ColumnInfos is not null)
        {
            expressionPrinter.Append("(");

            var isFirst = true;
            foreach (var column in ColumnInfos)
            {
                if (isFirst)
                {
                    isFirst = false;
                }
                else
                {
                    expressionPrinter.Append(", ");
                }

                expressionPrinter.Append(column.Name);

                if (column.TypeMapping is not null)
                {
                    expressionPrinter.Append(" ").Append(column.TypeMapping.StoreType);
                }
            }

            expressionPrinter.Append(")");
        }
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => ReferenceEquals(obj, this) || obj is DuckDBUnnestExpression e && Equals(e);

    /// <inheritdoc />
    public bool Equals(DuckDBUnnestExpression? expression)
        => base.Equals(expression)
           && (expression.ColumnInfos is null && ColumnInfos is null
               || expression.ColumnInfos is not null && ColumnInfos is not null
               && expression.ColumnInfos.SequenceEqual(ColumnInfos))
           && WithOrdinality == expression.WithOrdinality;

    /// <inheritdoc />
    public override int GetHashCode()
        => base.GetHashCode();

    private ColumnInfo? GetSingleColumnInfo()
        => ColumnInfos switch
        {
            null or [] => null,
            [var columnInfo] => columnInfo,
            _ => throw new InvalidOperationException(
                $"An unnest expression produces exactly one output column, but this one carries {ColumnInfos.Count} column infos.")
        };

    /// <summary>
    ///     Column descriptor for the output of a table-valued function.
    /// </summary>
    public readonly record struct ColumnInfo(string Name, RelationalTypeMapping? TypeMapping = null);
}
