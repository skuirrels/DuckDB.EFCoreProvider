using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     End-to-end coverage for BLOB literals and list reads, numeric Parse, byte-array Any, and the DateTimeOffset
///     members and constructors, TIMETZ reads, and JsonPathExists, on both the native DuckDB and the DuckLake profiles.
/// </summary>
public sealed class ValueTranslationRegressionTests : DuckDBTestBase
{
    private static readonly DateTimeOffset Stamp = new(2024, 3, 9, 22, 15, 30, TimeSpan.FromHours(3));
    private static readonly DateTime Clock = new(2024, 3, 9, 22, 15, 30);

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inline_blob_constant_renders_a_blob_literal(bool duckLake)
        => Run(duckLake, context =>
        {
            var query = context.Rows.Where(row => row.Data == new byte[] { 0x01, 0xAB, 0x00 });

            Assert.Contains(@"'\x01\xAB\x00'::BLOB", query.ToQueryString());
            Assert.Equal([1], query.Select(row => row.Id).ToArray());
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Byte_array_Any_translates_to_octet_length(bool duckLake)
        => Run(duckLake, context =>
        {
            var query = context.Rows.Where(row => row.Data.Any()).OrderBy(row => row.Id).Select(row => row.Id);

            Assert.Contains("octet_length", query.ToQueryString());
            Assert.Equal([1], query.ToArray());
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Blob_list_round_trips(bool duckLake)
        => Run(duckLake, context =>
        {
            var blobs = context.Rows.OrderBy(row => row.Id).Select(row => row.Blobs).ToArray();

            Assert.Equal([new byte[] { 0x01, 0x02 }, [], [0xFF]], blobs[0]);
            Assert.Empty(blobs[1]);
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Numeric_Parse_translates_to_a_cast(bool duckLake)
        => Run(duckLake, context =>
        {
            var query = context.Rows
                .Where(row => int.Parse(row.Number) == 42 && double.Parse(row.Number) > 41.5)
                .Select(row => row.Id);

            Assert.Contains("CAST", query.ToQueryString());
            Assert.Equal([1], query.ToArray());
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DateTimeOffset_UtcDateTime_and_LocalDateTime_translate(bool duckLake)
        => Run(duckLake, context =>
        {
            // LocalDateTime follows the session TimeZone; a fixed non-UTC zone keeps the check meaningful on
            // UTC machines.
            context.Database.OpenConnection();
            context.Database.ExecuteSqlRaw("SET TimeZone = 'Asia/Tokyo'");
            var query = context.Rows.Where(row => row.Id == 1)
                .Select(row => new { row.Stamp.UtcDateTime, row.Stamp.LocalDateTime });

            Assert.Contains("timezone('UTC'", query.ToQueryString());
            var result = query.Single();
            Assert.Equal(Stamp.UtcDateTime, result.UtcDateTime);
            Assert.Equal(Stamp.UtcDateTime.AddHours(9), result.LocalDateTime);
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DateTimeOffset_constructors_read_the_clock_time_at_the_offset(bool duckLake)
        => Run(duckLake, context =>
        {
            var result = context.Rows.Where(row => row.Id == 1)
                .Select(row => new
                {
                    Utc = new DateTimeOffset(row.Clock),
                    Ahead = new DateTimeOffset(row.Clock, TimeSpan.FromHours(3)),
                    Behind = new DateTimeOffset(row.Clock, TimeSpan.FromMinutes(-150))
                })
                .Single();

            Assert.Equal(new DateTimeOffset(Clock, TimeSpan.Zero), result.Utc);
            Assert.Equal(new DateTimeOffset(Clock, TimeSpan.FromHours(3)), result.Ahead);
            Assert.Equal(new DateTimeOffset(Clock, TimeSpan.FromMinutes(-150)), result.Behind);
        });

    [ConditionalFact]
    public void DateTimeOffset_constructor_with_a_non_constant_offset_is_not_translated()
        => Run(duckLake: false, context =>
        {
            var offset = TimeSpan.FromHours(1);

            Assert.Throws<InvalidOperationException>(() => context.Rows
                .Where(row => new DateTimeOffset(row.Clock, offset) > Stamp)
                .ToArray());
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimeOnly_mapped_to_TIMETZ_round_trips(bool duckLake)
        => Run(duckLake, context =>
        {
            Assert.Equal(
                [new TimeOnly(10, 5, 30), new TimeOnly(23, 59)],
                context.Rows.OrderBy(row => row.Id).Select(row => row.ClockTz).ToArray());
            Assert.Equal([2], context.Rows.Where(row => row.ClockTz == new TimeOnly(23, 59)).Select(row => row.Id).ToArray());
        });

#if NET11_0_OR_GREATER
    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonPathExists_treats_a_json_null_as_present_and_a_null_column_as_false(bool duckLake)
        => Run(duckLake, context =>
        {
            var filtered = context.Rows
                .Where(row => EF.Functions.JsonPathExists(row.Json!, "$.OptionalInt"))
                .Select(row => row.Id);

            Assert.Contains("json_type(", filtered.ToQueryString());
            Assert.Equal([1], filtered.ToArray());

            var projected = context.Rows.OrderBy(row => row.Id)
                .Select(row => new
                {
                    Present = EF.Functions.JsonPathExists(row.Json!, "$.OptionalInt"),
                    Missing = EF.Functions.JsonPathExists(row.Json!, "$.Other")
                })
                .ToArray();

            Assert.Equal([(true, false), (false, false)], projected.Select(row => (row.Present, row.Missing)));
        });
#endif

    private void Run(bool duckLake, Action<ValueContext> test)
    {
        var root = Path.Combine(Path.GetTempPath(), $"value_translation_{Guid.NewGuid():N}");
        try
        {
            DbContextOptions<ValueContext> options;
            if (duckLake)
            {
                var dataPath = Path.Combine(root, "data");
                Directory.CreateDirectory(dataPath);
                options = new DbContextOptionsBuilder<ValueContext>()
                    .UseDuckLake(Path.Combine(root, "catalog.ducklake"), duckLake => duckLake.DataPath(dataPath))
                    .EnableServiceProviderCaching(false)
                    .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                    .Options;
            }
            else
            {
                options = FileOptions<ValueContext>();
            }

            using var context = new ValueContext(options);
            context.Database.EnsureCreated();
            context.Rows.AddRange(
                new ValueRow
                {
                    Id = 1,
                    Data = [0x01, 0xAB, 0x00],
                    Blobs = [[0x01, 0x02], [], [0xFF]],
                    Number = "42",
                    Json = """{"OptionalInt": null}""",
                    Stamp = Stamp,
                    Clock = Clock,
                    ClockTz = new TimeOnly(10, 5, 30)
                },
                new ValueRow
                {
                    Id = 2,
                    Data = [],
                    Blobs = [],
                    Number = "7",
                    Stamp = Stamp.AddDays(1),
                    Clock = Clock.AddDays(1),
                    ClockTz = new TimeOnly(23, 59)
                });
            context.SaveChanges();
            context.ChangeTracker.Clear();

            test(context);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class ValueContext(DbContextOptions<ValueContext> options) : DbContext(options)
    {
        public DbSet<ValueRow> Rows => Set<ValueRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ValueRow>(
                entity =>
                {
                    entity.Property(row => row.Id).ValueGeneratedNever();
                    entity.Property(row => row.ClockTz).HasColumnType("TIMETZ");
                });
    }

    private sealed class ValueRow
    {
        public int Id { get; set; }

        public byte[] Data { get; set; } = [];

        public List<byte[]> Blobs { get; set; } = [];

        public string Number { get; set; } = "";

        public string? Json { get; set; }

        public DateTimeOffset Stamp { get; set; }

        public DateTime Clock { get; set; }

        public TimeOnly ClockTz { get; set; }
    }
}