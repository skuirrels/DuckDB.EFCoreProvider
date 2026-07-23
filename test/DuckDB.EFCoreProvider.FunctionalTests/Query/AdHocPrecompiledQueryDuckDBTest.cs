using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query;

public class AdHocPrecompiledQueryDuckDBTest : AdHocPrecompiledQueryRelationalTestBase
{
    public AdHocPrecompiledQueryDuckDBTest(NonSharedFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Projecting_entity_with_property_requiring_converter_with_closure_works()
    {
        return base.Projecting_entity_with_property_requiring_converter_with_closure_works();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Projecting_expression_requiring_converter_without_closure_works()
    {
        return base.Projecting_expression_requiring_converter_without_closure_works();
    }

    [ConditionalFact]
    public async Task File_source_preserves_custom_schema_qualified_function_metadata()
    {
        var options = (await InitializeAsync<FileSourceContext>(
            seed: async context =>
            {
                await context.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS analytics");
                await context.Database.ExecuteSqlRawAsync(
                    """CREATE OR REPLACE MACRO analytics.read_csv_auto(path) AS TABLE SELECT 42 AS "Id" """);
            })).GetOptions();

        await Test(
            """
            await using var context = new AdHocPrecompiledQueryDuckDBTest.FileSourceContext(dbContextOptions);

            var ids = await context.Rows.Select(row => row.Id).ToListAsync();

            Assert.Equal(new[] { 42 }, ids);
            """,
            typeof(FileSourceContext),
            options);
    }

    protected override bool AlwaysPrintGeneratedSources
        => false;

    protected override ITestStoreFactory TestStoreFactory
        => DuckDBTestStoreFactory.Instance;

    protected override PrecompiledQueryTestHelpers PrecompiledQueryTestHelpers
        => DuckDBPrecompiledQueryTestHelpers.Instance;

    public sealed class FileSourceContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<FileSourceRow> Rows => Set<FileSourceRow>();
    }

    [CustomFileSource("analytics.read_csv_auto", "unused.csv")]
    public sealed class FileSourceRow
    {
        public int Id { get; set; }
    }

    private sealed class CustomFileSourceAttribute(string function, string path) : DuckDBFileSourceAttribute(function, path);
}