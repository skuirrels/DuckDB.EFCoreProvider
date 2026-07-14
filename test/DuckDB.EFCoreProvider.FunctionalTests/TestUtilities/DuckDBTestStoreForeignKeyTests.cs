using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.EntityFrameworkCore.TestUtilities;

public class DuckDBTestStoreForeignKeyTests
{
    [Fact]
    public async Task Specification_store_creates_cyclic_relationship_model_without_weakening_production_generator()
    {
        await using var testStore = DuckDBTestStore.Create(nameof(Specification_store_creates_cyclic_relationship_model_without_weakening_production_generator));
        using var serviceProvider = DuckDBTestStoreFactory.Instance
            .AddProviderServices(new ServiceCollection())
            .BuildServiceProvider(validateScopes: true);
        var optionsBuilder = new DbContextOptionsBuilder<CyclicContext>();
        optionsBuilder.UseInternalServiceProvider(serviceProvider);
        testStore.AddProviderOptions(optionsBuilder);

        using var context = new CyclicContext(optionsBuilder.Options);

        Assert.IsType<DuckDBTestMigrationsSqlGenerator>(context.GetService<IMigrationsSqlGenerator>());
        context.Database.EnsureDeleted();
        Assert.True(context.Database.EnsureCreated());

        using var productionContext = new CyclicContext(
            new DbContextOptionsBuilder<CyclicContext>()
                .UseDuckDB("Data Source=:memory:")
                .Options);

        Assert.IsType<DuckDBMigrationsSqlGenerator>(productionContext.GetService<IMigrationsSqlGenerator>());
        var exception = Assert.Throws<NotSupportedException>(() => productionContext.Database.EnsureCreated());
        Assert.Contains(nameof(Migrations.Operations.AddForeignKeyOperation), exception.Message);
    }

    private sealed class CyclicContext(DbContextOptions<CyclicContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Left>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.HasOne<Right>()
                    .WithMany()
                    .HasForeignKey(item => item.RightId)
                    .OnDelete(DeleteBehavior.NoAction);
            });
            modelBuilder.Entity<Right>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.HasOne<Left>()
                    .WithMany()
                    .HasForeignKey(item => item.LeftId)
                    .OnDelete(DeleteBehavior.NoAction);
            });
        }
    }

    private sealed class Left
    {
        public int Id { get; set; }

        public int RightId { get; set; }
    }

    private sealed class Right
    {
        public int Id { get; set; }

        public int LeftId { get; set; }
    }
}
