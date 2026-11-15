using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Scaffolding;

// Compiled models rebuild every type mapping from its type's static Default, so each provider mapping that can
// appear in a model needs one that reproduces the store type, CLR type and DuckDB type of the original.
public partial class CompiledModelDuckDBTest
{
    private static readonly DateTime Clock = new(2024, 3, 9, 22, 15, 30, 123);
    private static readonly DateTimeOffset Instant = new(2024, 3, 9, 19, 15, 30, 456, TimeSpan.Zero);

    [ConditionalFact]
    public virtual Task Timestamp_and_time_mappings()
        => Test(
            modelBuilder => modelBuilder.Entity<TemporalRow>(
                entity =>
                {
                    entity.Property(row => row.Id).ValueGeneratedNever();
                    entity.Property(row => row.StampMs).HasColumnType("TIMESTAMP_MS");
                    entity.Property(row => row.StampNs).HasColumnType("TIMESTAMP_NS");
                    entity.Property(row => row.StampS).HasColumnType("TIMESTAMP_S");
                    entity.Property(row => row.ClockTz).HasColumnType("TIMETZ");
                }),
            model =>
            {
                var entityType = model.FindEntityType(typeof(TemporalRow))!;
                RelationalTypeMapping Mapping(string name)
                    => (RelationalTypeMapping)entityType.FindProperty(name)!.GetTypeMapping();

                Assert.StartsWith("TIMESTAMP_MS '", Mapping(nameof(TemporalRow.StampMs)).GenerateSqlLiteral(Clock));
                Assert.StartsWith("TIMESTAMP_NS '", Mapping(nameof(TemporalRow.StampNs)).GenerateSqlLiteral(Clock));
                Assert.StartsWith("TIMESTAMP_S '", Mapping(nameof(TemporalRow.StampS)).GenerateSqlLiteral(Clock));
                Assert.IsType<DuckDBTimestampTzTypeMapping>(Mapping(nameof(TemporalRow.Instant)));
                Assert.Equal(typeof(DateTimeOffset), Mapping(nameof(TemporalRow.Instant)).ClrType);
                Assert.StartsWith("TIMETZ '", Mapping(nameof(TemporalRow.ClockTz)).GenerateSqlLiteral(new TimeOnly(10, 5)));
                Assert.Equal(typeof(TimeSpan), Mapping(nameof(TemporalRow.Duration)).ClrType);
            },
            async context =>
            {
                var row = new TemporalRow
                {
                    Id = 1,
                    Stamp = Clock,
                    StampMs = Clock,
                    StampNs = Clock,
                    StampS = Clock.AddMilliseconds(-123),
                    Instant = Instant,
                    Time = TimeOnly.FromDateTime(Clock),
                    ClockTz = new TimeOnly(10, 5),
                    Duration = TimeSpan.FromMinutes(95)
                };
                context.Add(row);
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();

                // Constants are inlined as literals, so the query exercises each mapping's literal format too.
                var read = await context.Set<TemporalRow>()
                    .Where(r => r.StampMs == new DateTime(2024, 3, 9, 22, 15, 30, 123)
                        && r.StampS == new DateTime(2024, 3, 9, 22, 15, 30)
                        && r.Instant == new DateTimeOffset(2024, 3, 9, 19, 15, 30, 456, TimeSpan.Zero)
                        && r.Time == new TimeOnly(22, 15, 30, 123))
                    .SingleAsync();

                Assert.Equal(Clock, read.Stamp);
                Assert.Equal(Clock, read.StampMs);
                Assert.Equal(Clock, read.StampNs);
                Assert.Equal(Clock.AddMilliseconds(-123), read.StampS);
                Assert.Equal(Instant, read.Instant);
                Assert.Equal(TimeOnly.FromDateTime(Clock), read.Time);
                Assert.Equal(new TimeOnly(10, 5), read.ClockTz);
                Assert.Equal(TimeSpan.FromMinutes(95), read.Duration);
            });

    [ConditionalFact]
    public virtual Task Struct_mapping()
        => Test(
            modelBuilder => modelBuilder.Entity<StructRow>(
                entity =>
                {
                    entity.Property(row => row.Id).ValueGeneratedNever();
                    entity.ComplexProperty(row => row.Place);
                }),
            useContext: async context =>
            {
                context.Add(new StructRow { Id = 1, Place = new StructPlace { City = "Oslo", Since = Clock } });
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();

                var read = await context.Set<StructRow>().SingleAsync(r => r.Place.City == "Oslo");
                Assert.Equal(Clock, read.Place.Since);
            });

    [ConditionalFact]
    public virtual Task Json_mapping()
        => Test(
            modelBuilder => modelBuilder.Entity<JsonRow>(
                entity =>
                {
                    entity.Property(row => row.Id).ValueGeneratedNever();
                    entity.ComplexProperty(row => row.Detail, detail => detail.ToJson());
                }),
            useContext: async context =>
            {
                context.Add(new JsonRow { Id = 1, Detail = new JsonDetail { Name = "first", Count = 3 } });
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();

                var read = await context.Set<JsonRow>().SingleAsync(r => r.Detail.Count == 3);
                Assert.Equal("first", read.Detail.Name);
            });

    public class TemporalRow
    {
        public int Id { get; set; }
        public DateTime Stamp { get; set; }
        public DateTime StampMs { get; set; }
        public DateTime StampNs { get; set; }
        public DateTime StampS { get; set; }
        public DateTimeOffset Instant { get; set; }
        public TimeOnly Time { get; set; }
        public TimeOnly ClockTz { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public class StructRow
    {
        public int Id { get; set; }
        public StructPlace Place { get; set; } = null!;
    }

    public class StructPlace
    {
        public string City { get; set; } = "";
        public DateTime Since { get; set; }
    }

    public class JsonRow
    {
        public int Id { get; set; }
        public JsonDetail Detail { get; set; } = null!;
    }

    public class JsonDetail
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }
}