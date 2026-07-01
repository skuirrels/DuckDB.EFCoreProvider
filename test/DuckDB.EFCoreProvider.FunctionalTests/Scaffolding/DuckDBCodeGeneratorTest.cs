using System.Reflection;
using DuckDB.EFCoreProvider.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Scaffolding;

#pragma warning disable EF1001 // Internal EF Core API usage.

public class DuckDBCodeGeneratorTest
{
    [ConditionalFact]
    public void Use_provider_method_is_generated_correctly()
    {
        var codeGenerator = new DuckDBCodeGenerator(
            new ProviderCodeGeneratorDependencies(Enumerable.Empty<IProviderCodeGeneratorPlugin>()));

        var result = codeGenerator.GenerateUseProvider("Data Source=my_database.db", providerOptions: null);

        Assert.Equal("UseDuckDB", result.Method);
        Assert.Collection(
            result.Arguments,
            a => Assert.Equal("Data Source=my_database.db", a));
        Assert.Null(result.ChainedCall);
    }

    [ConditionalFact]
    public void Use_provider_method_is_generated_correctly_with_options()
    {
        var codeGenerator = new DuckDBCodeGenerator(
            new ProviderCodeGeneratorDependencies(Enumerable.Empty<IProviderCodeGeneratorPlugin>()));

        var providerOptions = new MethodCallCodeFragment(_setProviderOptionMethodInfo);

        var result = codeGenerator.GenerateUseProvider("Data Source=my_database.db", providerOptions);

        Assert.Equal("UseDuckDB", result.Method);
        Assert.Collection(
            result.Arguments,
            a => Assert.Equal("Data Source=my_database.db", a),
            a =>
            {
                var nestedClosure = Assert.IsType<NestedClosureCodeFragment>(a);

                Assert.Equal("x", nestedClosure.Parameter);
                Assert.Same(providerOptions, nestedClosure.MethodCalls[0]);
            });
        Assert.Null(result.ChainedCall);
    }

    private static readonly MethodInfo _setProviderOptionMethodInfo
        = typeof(DuckDBCodeGeneratorTest).GetRuntimeMethod(nameof(SetProviderOption), [typeof(object)])!;

    public static object SetProviderOption(object optionsBuilder)
        => optionsBuilder;
}
