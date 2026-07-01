using System.Reflection;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;

namespace DuckDB.EFCoreProvider.Scaffolding.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBCodeGenerator : ProviderCodeGenerator
{
    private static readonly MethodInfo UseDuckDBMethodInfo
        = typeof(DuckDBDbContextOptionsBuilderExtensions).GetRuntimeMethod(
            nameof(DuckDBDbContextOptionsBuilderExtensions.UseDuckDB),
            [typeof(DbContextOptionsBuilder), typeof(string), typeof(Action<DuckDBDbContextOptionsBuilder>)])!;

    public DuckDBCodeGenerator(ProviderCodeGeneratorDependencies dependencies) : base(dependencies)
    {
    }

    /// <inheritdoc />
    public override MethodCallCodeFragment GenerateUseProvider(string connectionString, MethodCallCodeFragment? providerOptions)
        => new(
            UseDuckDBMethodInfo,
            providerOptions == null
                ? [connectionString]
                : [connectionString, new NestedClosureCodeFragment("x", providerOptions)]);
}
