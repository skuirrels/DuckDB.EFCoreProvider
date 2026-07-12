using DuckDB.EFCoreProvider.Design.Internal;
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Microsoft.EntityFrameworkCore.TestUtilities;

public class DuckDBDatabaseCleaner : RelationalDatabaseCleaner
{
    // Dropping the owning tables removes their foreign keys. Generating separate DropForeignKey operations
    // would exercise DuckDB's unsupported in-place constraint path during test teardown.
    protected override bool AcceptForeignKey(DatabaseForeignKey foreignKey)
        => false;

    protected override bool AcceptIndex(DatabaseIndex index)
        => false;

    protected override bool AcceptTable(DatabaseTable table)
        => false;

    protected override string? BuildCustomSql(DatabaseModel databaseModel)
    {
        var remaining = databaseModel.Tables.ToList();
        var ordered = new List<DatabaseTable>(remaining.Count);
        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(candidate =>
                remaining.All(other => other == candidate || other.ForeignKeys.All(foreignKey => foreignKey.PrincipalTable != candidate)));
            if (next is null)
            {
                throw new InvalidOperationException("DuckDB test cleanup cannot order a cyclic foreign-key graph.");
            }

            ordered.Add(next);
            remaining.Remove(next);
        }

        var sql = new StringBuilder();
        foreach (var table in ordered)
        {
            sql.Append("DROP TABLE ");
            if (!string.IsNullOrEmpty(table.Schema))
            {
                sql.Append(Quote(table.Schema)).Append('.');
            }

            sql.Append(Quote(table.Name)).AppendLine(";");
        }

        return sql.ToString();

        static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    protected override IDatabaseModelFactory CreateDatabaseModelFactory(ILoggerFactory loggerFactory)
    {
        var services = new ServiceCollection();
        services.AddEntityFrameworkDuckDB();

        new DuckDBDesignTimeServices().ConfigureDesignTimeServices(services);

        return services
            .BuildServiceProvider()
            .GetRequiredService<IDatabaseModelFactory>();
    }

    public override void Clean(DatabaseFacade facade)
    {
        base.Clean(facade);
        facade.EnsureCreated();
    }
}