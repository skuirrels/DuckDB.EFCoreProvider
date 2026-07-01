using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class ValueConvertersEndToEndDuckDBTest : ValueConvertersEndToEndTestBase<ValueConvertersEndToEndDuckDBTest.ValueConvertersEndToEndDuckDBFixture>
{
    public ValueConvertersEndToEndDuckDBTest(ValueConvertersEndToEndDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_insert_and_read_back_with_conversions(int[] valueOrder)
    {
        return base.Can_insert_and_read_back_with_conversions(valueOrder);
    }

    public class ValueConvertersEndToEndDuckDBFixture : ValueConvertersEndToEndFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);

            modelBuilder.Entity<ConvertingEntity>(b =>
            {
                b.Property(e => e.NullableListOfInt).HasDefaultValue(new List<int>());
                b.Property(e => e.ListOfInt).HasDefaultValue(new List<int>());
                b.Property(e => e.NullableEnumerableOfInt).HasDefaultValue(Enumerable.Empty<int>());
                b.Property(e => e.EnumerableOfInt).HasDefaultValue(Enumerable.Empty<int>());
            });
        }
    }
}
