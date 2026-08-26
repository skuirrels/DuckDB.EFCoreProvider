using System.Data.Common;
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DuckDB.EFCoreProvider.Tests.Architecture;

// Unlike the Firebird provider, DuckDB.EFCoreProvider has real, deliberate native-only surface area
// (DuckLake attachment, encrypted-database attachment, the Quack remote profile, bulk Appender inserts,
// tiered-storage archiving) that inherently requires the concrete DuckDBConnection/DuckDBParameter
// types and can never be satisfied by a substituted ADO.NET provider -- so this suite does not attempt
// a blanket "no file may mention DuckDBConnection/DuckDBParameter" scan the way Firebird's did. Instead
// it scans only the one directory that must stay substitution-clean by construction (every mapped
// .NET type funnels through it) and separately proves the DI-level substitution contract.
public class ProviderFactoryBoundaryTests
{
    [Fact]
    public void Type_mappings_do_not_cast_their_parameter_to_the_concrete_ADO_parameter_type()
    {
        var typeMappingDirectory = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "DuckDB.EFCoreProvider", "Storage", "Internal"));

        var typeMappingFiles = Directory.GetFiles(typeMappingDirectory, "*TypeMapping.cs", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(typeMappingFiles);

        var offendingFiles = typeMappingFiles
            .Where(file => File.ReadAllText(file).Contains("(DuckDBParameter)parameter"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offendingFiles);
    }

    [Fact]
    public void AddEntityFrameworkDuckDB_preserves_a_registered_provider_factory()
    {
        var factory = new SubstituteProviderFactory();
        var services = new ServiceCollection();
        services.AddSingleton<DbProviderFactory>(factory);
        services.AddEntityFrameworkDuckDB();

        using var serviceProvider = services.BuildServiceProvider();
        Assert.Same(factory, serviceProvider.GetRequiredService<DbProviderFactory>());
    }

    private sealed class SubstituteProviderFactory : DbProviderFactory
    {
    }
}
