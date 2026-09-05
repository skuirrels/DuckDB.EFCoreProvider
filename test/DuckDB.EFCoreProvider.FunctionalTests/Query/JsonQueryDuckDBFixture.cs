using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query;

public class JsonQueryDuckDBFixture : JsonQueryRelationalFixture
{
#if NET11_0_OR_GREATER
    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);

        // EF11 adds nested primitive collections to this fixture. DuckDB's current mapping
        // supports one collection level; retain coverage of the existing JSON model.
        string[] nestedCollections =
        [
            "TestInt64CollectionCollection", "TestDoubleCollectionCollection", "TestSingleCollectionCollection",
            "TestBooleanCollectionCollection", "TestCharacterCollectionCollection", "TestDefaultStringCollectionCollection",
            "TestMaxLengthStringCollectionCollection", "TestInt16CollectionCollection", "TestInt32CollectionCollection",
            "TestNullableEnumWithIntConverterCollectionCollection", "TestNullableInt32CollectionCollection",
            "TestNullableEnumCollectionCollection"
        ];
        modelBuilder.Entity<TestModels.JsonQuery.JsonEntityAllTypes>(entity =>
        {
            foreach (var property in nestedCollections)
            {
                entity.Ignore(property);
            }

            entity.OwnsOne(value => value.Reference, owned =>
            {
                foreach (var property in nestedCollections)
                {
                    owned.Ignore(property);
                }
            });
            entity.OwnsMany(value => value.Collection, owned =>
            {
                foreach (var property in nestedCollections)
                {
                    owned.Ignore(property);
                }
            });
        });
    }
#endif

    protected override ITestStoreFactory TestStoreFactory
        => DuckDBTestStoreFactory.Instance;
}
