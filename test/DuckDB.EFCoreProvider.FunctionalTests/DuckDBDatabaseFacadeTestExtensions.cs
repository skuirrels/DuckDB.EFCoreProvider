using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore;

public static class DuckDBDatabaseFacadeTestExtensions
{
    public static void EnsureClean(this DatabaseFacade databaseFacade)
        => new DuckDBDatabaseCleaner().Clean(databaseFacade);
}
