using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query;

public class NorthwindQueryDuckDBFixture<TModelCustomizer> : NorthwindQueryRelationalFixture<TModelCustomizer>
    where TModelCustomizer : ITestModelCustomizer, new()
{
    protected override ITestStoreFactory TestStoreFactory
        => DuckDBNorthwindTestStoreFactory.Instance;

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);

        // NB: DuckDB doesn't support decimal very well. Using double instead
        modelBuilder.Entity<OrderDetail>().ToTable("OrderDetails");
        // modelBuilder.Entity<OrderDetail>().Property(o => o.UnitPrice).HasConversion<double>();
        // modelBuilder.Entity<Product>().Property(o => o.UnitPrice).HasConversion<double?>();
    }

    protected override Type ContextType
        => typeof(NorthwindDuckDBContext);
}