namespace Microsoft.EntityFrameworkCore.TestUtilities;

public class DuckDBNorthwindTestStoreFactory : DuckDBTestStoreFactory
{
    public static new DuckDBNorthwindTestStoreFactory Instance { get; } = new();

    protected DuckDBNorthwindTestStoreFactory()
    {
    }

    public override TestStore GetOrCreate(string storeName)
        => DuckDBTestStore.GetExisting("northwind");
}