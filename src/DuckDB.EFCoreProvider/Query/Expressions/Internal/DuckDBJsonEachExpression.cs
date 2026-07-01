using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Expressions.Internal;

/// <summary>
///     An expression that represents a DuckDB json_each function call in a SQL tree.
/// </summary>
/// <remarks>
///     <para>
///         See <see href="https://duckdb.org/docs/lts/data/json/json_functions#json-table-functions">json_each</see> for more information.
///     </para>
///     <para>
///         This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///         the same compatibility standards as public APIs. It may be changed or removed without notice in
///         any release. You should only use it directly in your code with extreme caution and knowing that
///         doing so can result in application failures when updating to a new Entity Framework Core release.
///     </para>
/// </remarks>
public class DuckDBJsonEachExpression : TableValuedFunctionExpression
{
    private static ConstructorInfo? _quotingConstructor;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual SqlExpression JsonExpression
        => Arguments[0];

    public virtual IReadOnlyList<PathSegment>? Path { get; }

    public DuckDBJsonEachExpression(
        string alias,
        SqlExpression jsonExpression,
        IReadOnlyList<PathSegment>? path = null)
        : base(alias, "json_each", schema: null, builtIn: true, [jsonExpression])
        => Path = path;

    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        var visitedJsonExpression = (SqlExpression)visitor.Visit(JsonExpression);

        PathSegment[]? visitedPath = null;

        if (Path is not null)
        {
            for (var i = 0; i < Path.Count; i++)
            {
                var segment = Path[i];
                PathSegment newSegment;

                if (segment.PropertyName is not null)
                {
                    // PropertyName segments are (currently) constants, nothing to visit.
                    newSegment = segment;
                }
                else
                {
                    var newArrayIndex = (SqlExpression)visitor.Visit(segment.ArrayIndex)!;
                    if (newArrayIndex == segment.ArrayIndex)
                    {
                        newSegment = segment;
                    }
                    else
                    {
                        newSegment = new PathSegment(newArrayIndex);

                        if (visitedPath is null)
                        {
                            visitedPath = new PathSegment[Path.Count];
                            for (var j = 0; j < i; i++)
                            {
                                visitedPath[j] = Path[j];
                            }
                        }
                    }
                }

                if (visitedPath is not null)
                {
                    visitedPath[i] = newSegment;
                }
            }
        }

        return Update(visitedJsonExpression, visitedPath ?? Path);
    }

    public virtual DuckDBJsonEachExpression Update(
        SqlExpression jsonExpression,
        IReadOnlyList<PathSegment>? path)
        => jsonExpression == JsonExpression
            && (ReferenceEquals(path, Path) || path is not null && Path is not null && path.SequenceEqual(Path))
                ? this
                : new DuckDBJsonEachExpression(Alias, jsonExpression, path);

    public override TableExpressionBase Clone(string? alias, ExpressionVisitor cloningExpressionVisitor)
    {
        var newJsonExpression = (SqlExpression)cloningExpressionVisitor.Visit(JsonExpression);
        var clone = new DuckDBJsonEachExpression(alias!, newJsonExpression, Path);

        foreach (var annotation in GetAnnotations())
        {
            clone.AddAnnotation(annotation.Name, annotation.Value);
        }

        return clone;
    }

    public override DuckDBJsonEachExpression WithAlias(string newAlias)
        => new(newAlias, JsonExpression, Path);

    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DuckDBJsonEachExpression).GetConstructor(
            [
                typeof(string),
                typeof(SqlExpression),
                typeof(IReadOnlyList<PathSegment>)
            ])!,
            Constant(Alias, typeof(string)),
            JsonExpression.Quote(),
            Path is null
                ? Constant(null, typeof(IReadOnlyList<PathSegment>))
                : NewArrayInit(typeof(PathSegment), Path.Select(s => s.Quote())));

    protected override void Print(ExpressionPrinter expressionPrinter)
    {
        expressionPrinter.Append(Name);
        expressionPrinter.Append("(");
        expressionPrinter.Visit(JsonExpression);

        if (Path is not null)
        {
            expressionPrinter
                .Append(", '")
                .Append(string.Join(".", Path.Select(e => e.ToString())))
                .Append("'");
        }

        expressionPrinter.Append(")");

        PrintAnnotations(expressionPrinter);

        expressionPrinter.Append(" AS ");
        expressionPrinter.Append(Alias);
    }

    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj) || (obj is DuckDBJsonEachExpression jsonEachExpression && Equals(jsonEachExpression));

    private bool Equals(DuckDBJsonEachExpression other)
        => base.Equals(other)
            && (ReferenceEquals(Path, other.Path)
                || (Path is not null && other.Path is not null && Path.SequenceEqual(other.Path)));

    public override int GetHashCode()
        => base.GetHashCode();
}