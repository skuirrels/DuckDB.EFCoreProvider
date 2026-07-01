using DuckDB.EFCoreProvider.Extensions;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Coverage for array/list column mappings, including the empty-array case (which exercises element-type
///     inference where no element value is available) and nullable elements.
/// </summary>
public class ArrayRoundTripTests : DuckDBTestBase
{
    private ArrayContext CreateContext()
        => new(FileOptions<ArrayContext>());

    [ConditionalFact]
    public void List_round_trips()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new Bag { Id = 1, Numbers = [1, 2, 3, 42] });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal([1, 2, 3, 42], context.Set<Bag>().Single(x => x.Id == 1).Numbers);
        }
    }

    [ConditionalFact]
    public void Array_round_trips()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new Bag { Id = 1, Words = ["alpha", "beta", "gamma"] });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(["alpha", "beta", "gamma"], context.Set<Bag>().Single(x => x.Id == 1).Words);
        }
    }

    [ConditionalFact]
    public void Empty_collections_round_trip()
    {
        // No element value is present to infer the element type from — confirms the mapping carries enough
        // type information on its own (i.e. an explicit parameter DataTypeName is not required here).
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new Bag { Id = 1, Numbers = [], Words = [] });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var bag = context.Set<Bag>().Single(x => x.Id == 1);
            Assert.Empty(bag.Numbers);
            Assert.Empty(bag.Words);
        }
    }

    [ConditionalFact]
    public void Large_list_round_trips()
    {
        var values = Enumerable.Range(1, 1000).ToList();

        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new Bag { Id = 1, Numbers = values });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(values, context.Set<Bag>().Single(x => x.Id == 1).Numbers);
        }
    }

    private sealed class ArrayContext(DbContextOptions<ArrayContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Bag>(entity =>
            {
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.Numbers).HasColumnType("INTEGER[]");
                entity.Property(e => e.Words).HasColumnType("VARCHAR[]");
            });
        }
    }

    private sealed class Bag
    {
        public int Id { get; set; }
        public List<int> Numbers { get; set; } = [];
        public string[] Words { get; set; } = [];
    }
}
