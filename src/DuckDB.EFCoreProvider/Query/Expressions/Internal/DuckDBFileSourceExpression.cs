using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
        SqlExpression path)
        : this(alias, function, path, annotations: null)
    {
    }

    private DuckDBFileSourceExpression(
        string alias,
        DuckDBFileSourceFunction function,
        SqlExpression path,
        IReadOnlyDictionary<string, IAnnotation>? annotations)
        : base(
            alias,
            function.Name,
            function.Schema,
            function.IsBuiltIn,
            [path],
            annotations)
        => Function = function;

    public DuckDBFileSourceFunction Function { get; }

    public SqlExpression Path => (SqlExpression)Arguments[0];

    public override Expression Quote()
        => New(
            _quotingConstructor ??= typeof(DuckDBFileSourceExpression).GetConstructor(
                [typeof(string), typeof(DuckDBFileSourceFunction), typeof(SqlExpression)])!,
            Constant(Alias, typeof(string)),
            Constant(Function),
            Path.Quote());

#if NET11_0_OR_GREATER
    public override TableValuedFunctionExpression Update(IReadOnlyList<Expression> arguments)
#else
    public override TableValuedFunctionExpression Update(IReadOnlyList<SqlExpression> arguments)
#endif
        => arguments is [SqlExpression path]
            ? path == Path
                ? this
                : new DuckDBFileSourceExpression(Alias, Function, path, Annotations)
            : throw new ArgumentException(
                $"A DuckDB file source takes exactly one path argument, but {arguments.Count} were supplied.",
                nameof(arguments));

    public override TableExpressionBase Clone(string? alias, ExpressionVisitor cloningExpressionVisitor)
        => new DuckDBFileSourceExpression(
            alias ?? Alias,
            Function,
            (SqlExpression)cloningExpressionVisitor.Visit(Path),
            Annotations);

    public override TableValuedFunctionExpression WithAlias(string newAlias)
        => new DuckDBFileSourceExpression(newAlias, Function, Path, Annotations);

    protected override TableValuedFunctionExpression WithAnnotations(IReadOnlyDictionary<string, IAnnotation> annotations)
        => new DuckDBFileSourceExpression(Alias, Function, Path, annotations);
}
