using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBQueryCompilationContextFactory : RelationalQueryCompilationContextFactory
{
    private readonly QueryCompilationContextDependencies _compilationContextDependencies;
    private readonly RelationalQueryCompilationContextDependencies _relationalQueryCompilationContextDependencies;

    public DuckDBQueryCompilationContextFactory(
        QueryCompilationContextDependencies compilationContextDependencies,
        RelationalQueryCompilationContextDependencies relationalQueryCompilationContextDependencies)
        : base(compilationContextDependencies, relationalQueryCompilationContextDependencies)
    {
        _compilationContextDependencies = compilationContextDependencies;
        _relationalQueryCompilationContextDependencies = relationalQueryCompilationContextDependencies;
    }

    /// <inheritdoc />
    public override QueryCompilationContext Create(bool async)
    {
        return new DuckDBQueryCompilationContext(
            _compilationContextDependencies,
            _relationalQueryCompilationContextDependencies,
            async);
    }

    /// <inheritdoc />
    [Experimental("EF9100")]
    public override QueryCompilationContext CreatePrecompiled(bool async)
    {
        return new DuckDBQueryCompilationContext(
            _compilationContextDependencies,
            _relationalQueryCompilationContextDependencies,
            async,
            precompiling: true);
    }
}
