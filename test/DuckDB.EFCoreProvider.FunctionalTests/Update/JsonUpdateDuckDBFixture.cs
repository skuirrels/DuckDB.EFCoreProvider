using Microsoft.EntityFrameworkCore.TestModels.JsonQuery;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Update;

public class JsonUpdateDuckDBFixture : JsonUpdateFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory
        => DuckDBTestStoreFactory.Instance;

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);

        modelBuilder.Entity<JsonEntityAllTypes>(b =>
        {
            b.Ignore(e => e.TestInt64CollectionCollection);
            b.Ignore(e => e.TestDoubleCollectionCollection);
            b.Ignore(e => e.TestSingleCollectionCollection);
            b.Ignore(e => e.TestBooleanCollectionCollection);
            b.Ignore(e => e.TestCharacterCollectionCollection);
            b.Ignore(e => e.TestDefaultStringCollectionCollection);
            b.Ignore(e => e.TestMaxLengthStringCollectionCollection);
            b.Ignore(e => e.TestInt16CollectionCollection);
            b.Ignore(e => e.TestInt32CollectionCollection);
            b.Ignore(e => e.TestNullableEnumWithIntConverterCollectionCollection);
            b.Ignore(e => e.TestNullableInt32CollectionCollection);
            b.Ignore(e => e.TestNullableEnumCollectionCollection);

            b.OwnsOne(
                e => e.Reference, b =>
                {
                    b.Ignore(e => e.TestInt64CollectionCollection);
                    b.Ignore(e => e.TestDoubleCollectionCollection);
                    b.Ignore(e => e.TestSingleCollectionCollection);
                    b.Ignore(e => e.TestBooleanCollectionCollection);
                    b.Ignore(e => e.TestCharacterCollectionCollection);
                    b.Ignore(e => e.TestDefaultStringCollectionCollection);
                    b.Ignore(e => e.TestMaxLengthStringCollectionCollection);
                    b.Ignore(e => e.TestInt16CollectionCollection);
                    b.Ignore(e => e.TestInt32CollectionCollection);
                    b.Ignore(e => e.TestNullableEnumWithIntConverterCollectionCollection);
                    b.Ignore(e => e.TestNullableInt32CollectionCollection);
                    b.Ignore(e => e.TestNullableEnumCollectionCollection);
                });
            b.OwnsMany(
                x => x.Collection, b =>
                {
                    b.Ignore(e => e.TestInt64CollectionCollection);
                    b.Ignore(e => e.TestDoubleCollectionCollection);
                    b.Ignore(e => e.TestSingleCollectionCollection);
                    b.Ignore(e => e.TestBooleanCollectionCollection);
                    b.Ignore(e => e.TestCharacterCollectionCollection);
                    b.Ignore(e => e.TestDefaultStringCollectionCollection);
                    b.Ignore(e => e.TestMaxLengthStringCollectionCollection);
                    b.Ignore(e => e.TestInt16CollectionCollection);
                    b.Ignore(e => e.TestInt32CollectionCollection);
                    b.Ignore(e => e.TestNullableEnumWithIntConverterCollectionCollection);
                    b.Ignore(e => e.TestNullableInt32CollectionCollection);
                    b.Ignore(e => e.TestNullableEnumCollectionCollection);
                });
        });
    }
}