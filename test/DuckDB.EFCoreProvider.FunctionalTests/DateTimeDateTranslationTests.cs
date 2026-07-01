using DuckDB.EFCoreProvider.Extensions;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Regression tests for translating <see cref="DateTime.Date" />, which must produce a
///     <see cref="DateTime" />-typed result (midnight), not a <see cref="DateOnly" />. Projecting or grouping
///     by <c>.Date</c> previously threw
///     "No coercion operator is defined between types 'System.DateOnly' and 'System.DateTime'".
/// </summary>
public class DateTimeDateTranslationTests : DuckDBTestBase
{
    private EventContext CreateContext()
        => new(FileOptions<EventContext>());

    private void Seed()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();
        context.Events.AddRange(
            new Event { Id = 1, At = new DateTime(2026, 6, 3, 14, 35, 22), NullableAt = new DateTime(2026, 6, 3, 9, 0, 0) },
            new Event { Id = 2, At = new DateTime(2026, 6, 3, 8, 10, 0), NullableAt = new DateTime(2026, 6, 3, 23, 59, 0) },
            new Event { Id = 3, At = new DateTime(2026, 6, 4, 1, 0, 0), NullableAt = null });
        context.SaveChanges();
    }

    [ConditionalFact]
    public void Project_Date_returns_DateTime_at_midnight()
    {
        Seed();
        using var context = CreateContext();

        var dates = context.Events.OrderBy(e => e.Id).Select(e => e.At.Date).ToList();

        Assert.Equal(new DateTime(2026, 6, 3), dates[0]);
        Assert.Equal(new DateTime(2026, 6, 3), dates[1]);
        Assert.Equal(new DateTime(2026, 6, 4), dates[2]);
        Assert.All(dates, d => Assert.Equal(TimeSpan.Zero, d.TimeOfDay));
    }

    [ConditionalFact]
    public void GroupBy_Date_does_not_throw_coercion_error()
    {
        Seed();
        using var context = CreateContext();

        // This is the panel/chart aggregation shape that previously failed during shaper compilation.
        var grouped = context.Events
            .GroupBy(e => e.At.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderBy(x => x.Day)
            .ToList();

        Assert.Equal(2, grouped.Count);
        Assert.Equal(new DateTime(2026, 6, 3), grouped[0].Day);
        Assert.Equal(2, grouped[0].Count);
        Assert.Equal(new DateTime(2026, 6, 4), grouped[1].Day);
        Assert.Equal(1, grouped[1].Count);
    }

    [ConditionalFact]
    public void GroupBy_nullable_Date_does_not_throw_coercion_error()
    {
        Seed();
        using var context = CreateContext();

        // Mirrors a nullable DateTime? projection (e.g. Shipment.ETD.Value.Date).
        var grouped = context.Events
            .Where(e => e.NullableAt != null)
            .GroupBy(e => e.NullableAt!.Value.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderBy(x => x.Day)
            .ToList();

        Assert.Single(grouped);
        Assert.Equal(new DateTime(2026, 6, 3), grouped[0].Day);
        Assert.Equal(2, grouped[0].Count);
    }

    [ConditionalFact]
    public void Filter_by_Date_equality_works()
    {
        Seed();
        using var context = CreateContext();

        var count = context.Events.Count(e => e.At.Date == new DateTime(2026, 6, 3));

        Assert.Equal(2, count);
    }

    private sealed class EventContext(DbContextOptions<EventContext> options) : DbContext(options)
    {
        public DbSet<Event> Events => Set<Event>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Event>().Property(e => e.Id).ValueGeneratedNever();
    }

    private sealed class Event
    {
        public int Id { get; set; }
        public DateTime At { get; set; }
        public DateTime? NullableAt { get; set; }
    }
}
