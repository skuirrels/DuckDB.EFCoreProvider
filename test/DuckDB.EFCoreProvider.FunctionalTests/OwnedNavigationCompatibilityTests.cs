using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class OwnedNavigationCompatibilityTests
{
    [Fact]
    public async Task Separately_mapped_views_require_explicit_owned_reference_loading()
    {
        using var connection = new DuckDBConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new ViewContext(
            new DbContextOptionsBuilder<ViewContext>().UseDuckDB(connection)
                .EnableServiceProviderCaching(false)
                .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)).Options);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE VIEW OwnersView AS SELECT 1 AS Id;
            CREATE VIEW DetailsView AS SELECT 1 AS OwnerId, 17 AS Number, true AS Enabled;
            """);

        var owner = context.Model.FindEntityType(typeof(ViewRow))!;
        var navigation = owner.FindNavigation(nameof(ViewRow.Details))!;
        Assert.Null(owner.GetTableName());
        Assert.Null(navigation.TargetEntityType.GetTableName());
        Assert.False(navigation.IsEagerLoaded);

        var query = context.Set<ViewRow>().AsNoTracking();
        Assert.DoesNotContain("JOIN", query.ToQueryString());
        Assert.Null((await query.SingleAsync()).Details);

        var included = await query.Include(row => row.Details).SingleAsync();
        Assert.NotNull(included.Details);
        Assert.Equal(17, included.Details.Number);
        Assert.True(included.Details.Enabled);
    }

    [Fact]
    public void Owned_reference_loading_respects_explicit_configuration()
    {
        using var context = new OwnedContext(
            new DbContextOptionsBuilder<OwnedContext>().UseDuckDB("Data Source=:memory:").EnableServiceProviderCaching(false)
                .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)).Options);

        Assert.True(context.Model.FindEntityType(typeof(FirstRow))!.FindNavigation(nameof(FirstRow.Details))!.IsEagerLoaded);
        Assert.False(context.Model.FindEntityType(typeof(SecondRow))!.FindNavigation(nameof(SecondRow.Details))!.IsEagerLoaded);
    }

    [Theory]
    [InlineData(17, true)]
#if NET11_0_OR_GREATER
    // EF11 fixes upstream #37525: assigning default values to an optional owned TPH dependent.
    [InlineData(0, false)]
#endif
    public async Task Optional_owned_values_are_loaded_from_a_shared_TPH_table(int number, bool enabled)
    {
        using var connection = new DuckDBConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OwnedContext>().UseDuckDB(connection).EnableServiceProviderCaching(false)
                .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)).Options;
        await using var context = new OwnedContext(options);
        await context.Database.EnsureCreatedAsync();
        context.Add(new FirstRow { Id = 1 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var row = await context.Set<FirstRow>().SingleAsync();
        Assert.Null(row.Details);
        row.Details = new Details { Number = number, Enabled = enabled };
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        row = await context.Set<FirstRow>().SingleAsync();
        Assert.NotNull(row.Details);
        Assert.Equal(number, row.Details.Number);
        Assert.Equal(enabled, row.Details.Enabled);
    }

    private sealed class OwnedContext(DbContextOptions<OwnedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Row>().Property(row => row.Id).ValueGeneratedNever();
            modelBuilder.Entity<FirstRow>().OwnsOne(row => row.Details, owned =>
            {
                owned.Property(value => value.Number).HasColumnName("DetailsNumber");
                owned.Property(value => value.Enabled).HasColumnName("DetailsEnabled");
            });
            modelBuilder.Entity<SecondRow>().OwnsOne(row => row.Details, owned =>
            {
                owned.Property(value => value.Number).HasColumnName("DetailsNumber");
                owned.Property(value => value.Enabled).HasColumnName("DetailsEnabled");
            });
            modelBuilder.Entity<SecondRow>().Navigation(row => row.Details).AutoInclude(false);
        }
    }

    private sealed class ViewContext(DbContextOptions<ViewContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ViewRow>().ToView("OwnersView");
            modelBuilder.Entity<ViewRow>().OwnsOne(row => row.Details, owned =>
            {
                owned.ToView("DetailsView");
                owned.WithOwner().HasForeignKey("OwnerId");
            });
        }
    }

    private sealed class ViewRow { public int Id { get; set; } public Details? Details { get; set; } }
    private abstract class Row { public int Id { get; set; } }
    private sealed class FirstRow : Row { public Details? Details { get; set; } }
    private sealed class SecondRow : Row { public Details? Details { get; set; } }
    private sealed class Details
    {
        public int Number { get; set; }
        public bool Enabled { get; set; }
    }
}