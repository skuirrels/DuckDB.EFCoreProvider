using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Expressions.Internal;

internal sealed class DuckDBFileSourceExpression : TableValuedFunctionExpression
{
    private static ConstructorInfo? _quotingConstructor;

    public DuckDBFileSourceExpression(
        string alias,
        DuckDBFileSourceFunction function,
        SqlExpression path,
        IReadOnlyList<IEntityType>? entityTypes = null)
        : this(alias, function, path, entityTypes, annotations: null)
    {
    }

    private DuckDBFileSourceExpression(
        string alias,
        DuckDBFileSourceFunction function,
        SqlExpression path,
        IReadOnlyList<IEntityType>? entityTypes,
        IReadOnlyDictionary<string, IAnnotation>? annotations)
        : base(
            alias,
            function.Name,
            function.Schema,
            function.IsBuiltIn,
            [path],
            annotations)
    {
        Function = function;
        EntityTypes = entityTypes ?? [];
    }

    public DuckDBFileSourceFunction Function { get; }

    public SqlExpression Path => Arguments[0];

    public IReadOnlyList<IEntityType> EntityTypes { get; }

    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DuckDBFileSourceExpression).GetConstructor(
                [
                    typeof(string),
                    typeof(DuckDBFileSourceFunction),
                    typeof(SqlExpression),
                    typeof(IReadOnlyList<IEntityType>)
                ])!,
            Constant(Alias, typeof(string)),
            Constant(Function),
            Path.Quote(),
            Constant(EntityTypes));

    public override TableValuedFunctionExpression Update(IReadOnlyList<SqlExpression> arguments)
        => arguments is [var path]
            ? path == Path
                ? this
                : new DuckDBFileSourceExpression(Alias, Function, path, EntityTypes, Annotations)
            : throw new ArgumentException(
                $"A DuckDB file source takes exactly one path argument, but {arguments.Count} were supplied.",
                nameof(arguments));

    public override TableExpressionBase Clone(string? alias, ExpressionVisitor cloningExpressionVisitor)
        => new DuckDBFileSourceExpression(
            alias ?? Alias,
            Function,
            (SqlExpression)cloningExpressionVisitor.Visit(Path),
            EntityTypes,
            Annotations);

    public override TableValuedFunctionExpression WithAlias(string newAlias)
        => new DuckDBFileSourceExpression(newAlias, Function, Path, EntityTypes, Annotations);

    protected override TableValuedFunctionExpression WithAnnotations(IReadOnlyDictionary<string, IAnnotation> annotations)
        => new DuckDBFileSourceExpression(Alias, Function, Path, EntityTypes, annotations);
}