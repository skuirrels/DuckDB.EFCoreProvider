using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore.TestUtilities;

/// <summary>
///     Generates schemas for EF's generic relational specification stores without physical foreign keys.
/// </summary>
/// <remarks>
///     The specification models contain cyclic relationship graphs that DuckDB cannot represent physically.
///     Production contexts continue to use <see cref="DuckDBMigrationsSqlGenerator" /> and retain its normal
///     inline-foreign-key and table-rebuild behavior.
/// </remarks>
public sealed class DuckDBTestMigrationsSqlGenerator(MigrationsSqlGeneratorDependencies dependencies)
    : DuckDBMigrationsSqlGenerator(dependencies)
{
    protected override void Generate(
        AddForeignKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
    }

    protected override void Generate(
        DropForeignKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
    }

    protected override void CreateTableForeignKeys(
        CreateTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
    }
}

public static class DuckDBTestMigrationsServiceCollectionExtensions
{
    public static IServiceCollection AddDuckDBTestStoreServices(this IServiceCollection services)
        => services
            .AddEntityFrameworkDuckDB()
            .AddScoped<IMigrationsSqlGenerator, DuckDBTestMigrationsSqlGenerator>();
}
