using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class PrecompiledQueryDuckDBTest : PrecompiledQueryRelationalTestBase, IClassFixture<PrecompiledQueryDuckDBTest.PrecompiledQueryDuckDBFixture>
{
    public PrecompiledQueryDuckDBTest(PrecompiledQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task BinaryExpression()
    {
        return base.BinaryExpression();
    }

    [Fact]
    public Task FromSqlRaw_rebinds_argument_array_values_and_nulls()
        => Test(
            """"
foreach (int? minimum in new int?[] { 8, 9, null })
{
    object[] arguments = [minimum];
    var blogs = await context.Blogs
        .FromSqlRaw("""SELECT * FROM "Blogs" WHERE {0} IS NULL OR "Id" >= {0}""", arguments)
        .OrderBy(blog => blog.Id)
        .ToListAsync();
    Assert.Equal(minimum == 9 ? new[] { 9 } : new[] { 8, 9 }, blogs.Select(blog => blog.Id));
}
"""",
            interceptorCodeAsserter: code => Assert.Contains("RelationalCommandCache", code));

    [Fact]
    public Task FromSql_rebinds_interpolated_values_with_LINQ_composition()
        => Test(
            """"
foreach (var minimum in new[] { 8, 9 })
{
    var blogs = await context.Blogs
        .FromSql($"""SELECT * FROM "Blogs" WHERE "Id" >= {minimum}""")
        .Where(blog => blog.Id < 10)
        .OrderBy(blog => blog.Id)
        .ToListAsync();
    Assert.Equal(minimum == 9 ? new[] { 9 } : new[] { 8, 9 }, blogs.Select(blog => blog.Id));
}
"""",
            interceptorCodeAsserter: code => Assert.Contains("RelationalCommandCache", code));

    public class PrecompiledQueryDuckDBFixture : PrecompiledQueryRelationalFixture
    {
#if NET11_0_OR_GREATER
        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);
            // EF11 seeds the JSON null token to test JSON collection precompilation.
            modelBuilder.Entity<PrecompiledQueryRelationalTestBase.EntityWithPrimitiveCollection>()
                .PrimitiveCollection(entity => entity.Tags).HasColumnType("JSON");
        }
#endif

        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        public override PrecompiledQueryTestHelpers PrecompiledQueryTestHelpers
            => DuckDBPrecompiledQueryTestHelpers.Instance;
    }
}
