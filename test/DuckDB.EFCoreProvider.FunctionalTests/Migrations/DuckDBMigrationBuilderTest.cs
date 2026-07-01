using DuckDB.EFCoreProvider.Extensions;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Migrations;

public class DuckDBMigrationBuilderTest
{
    [ConditionalFact]
    public void IsDuckDB_when_using_DuckDB()
    {
        var migrationBuilder = new MigrationBuilder("DuckDB.EFCoreProvider");
        Assert.True(migrationBuilder.IsDuckDB());
    }

    [ConditionalFact]
    public void Not_IsDuckDB_when_using_different_provider()
    {
        var migrationBuilder = new MigrationBuilder("Microsoft.EntityFrameworkCore.InMemory");
        Assert.False(migrationBuilder.IsDuckDB());
    }
}
