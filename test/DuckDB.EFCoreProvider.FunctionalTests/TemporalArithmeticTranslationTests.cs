using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     End-to-end coverage for temporal arithmetic: <see cref="TimeSpan" /> totals of a <see cref="DateTime" /> or
///     <see cref="DateTimeOffset" /> subtraction, the fractional <c>Add*</c> methods, and <c>DayOfWeek</c>. Each result
///     is compared with the same expression evaluated by .NET over the seeded rows, on both backend profiles.
/// </summary>
public sealed class TemporalArithmeticTranslationTests : DuckDBTestBase
{
    // Row 1: 2.23 days apart. Row 2: two hours across midnight. Row 3: later is before earlier. Row 4: null operands.
    private static readonly TemporalRow[] Rows =
    [
        new()
        {
            Id = 1,
            Earlier = new DateTime(2026, 1, 1, 0, 0, 0),
            Later = new DateTime(2026, 1, 3, 5, 31, 12),
            NullableEarlier = new DateTime(2026, 1, 1, 0, 0, 0),
            NullableLater = new DateTime(2026, 1, 3, 5, 31, 12),
            EarlierOffset = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(2)),
            LaterOffset = new DateTimeOffset(2026, 1, 3, 12, 31, 12, TimeSpan.Zero),
            NullableLaterOffset = new DateTimeOffset(2026, 1, 3, 12, 31, 12, TimeSpan.Zero)
        },
        new()
        {
            Id = 2,
            Earlier = new DateTime(2026, 1, 1, 23, 0, 0),
            Later = new DateTime(2026, 1, 2, 1, 0, 0),
            NullableEarlier = new DateTime(2026, 1, 1, 23, 0, 0),
            NullableLater = new DateTime(2026, 1, 2, 1, 0, 0),
            EarlierOffset = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
            LaterOffset = new DateTimeOffset(2026, 1, 1, 14, 0, 0, TimeSpan.Zero),
            NullableLaterOffset = new DateTimeOffset(2026, 1, 1, 14, 0, 0, TimeSpan.Zero)
        },
        new()
        {
            Id = 3,
            Earlier = new DateTime(2026, 1, 5, 12, 0, 0),
            Later = new DateTime(2026, 1, 4, 6, 0, 0, 250),
            NullableEarlier = new DateTime(2026, 1, 5, 12, 0, 0),
            NullableLater = new DateTime(2026, 1, 4, 6, 0, 0, 250),
            EarlierOffset = new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero),
            LaterOffset = new DateTimeOffset(2026, 1, 4, 7, 0, 0, TimeSpan.FromHours(-5)),
            NullableLaterOffset = new DateTimeOffset(2026, 1, 4, 7, 0, 0, TimeSpan.FromHours(-5))
        },
        new()
        {
            Id = 4,
            Earlier = new DateTime(2026, 1, 4, 12, 0, 0),
            Later = new DateTime(2026, 1, 4, 12, 0, 0),
            EarlierOffset = new DateTimeOffset(2026, 1, 4, 12, 0, 0, TimeSpan.Zero),
            LaterOffset = new DateTimeOffset(2026, 1, 4, 12, 0, 0, TimeSpan.Zero)
        }
    ];

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DateTime_subtraction_totals_match_dotnet(bool duckLake)
        => Run(duckLake, context =>
        {
            var query = context.Rows.OrderBy(row => row.Id).Select(row => new
            {
                Days = (row.Later - row.Earlier).TotalDays,
                Hours = (row.Later - row.Earlier).TotalHours,
                Minutes = (row.Later - row.Earlier).TotalMinutes,
                Seconds = (row.Later - row.Earlier).TotalSeconds,
                Milliseconds = (row.Later - row.Earlier).TotalMilliseconds,
                Microseconds = (row.Later - row.Earlier).TotalMicroseconds,
                Nanoseconds = (row.Later - row.Earlier).TotalNanoseconds
            });

            var sql = query.ToQueryString();
            var actual = query.ToArray();

            Assert.Contains("epoch_us", sql);
            Assert.DoesNotContain("date_diff", sql);
            Assert.Equal(Rows.Length, actual.Length);
            for (var i = 0; i < Rows.Length; i++)
            {
                var expected = Rows[i].Later - Rows[i].Earlier;
                AssertClose(expected.TotalDays, actual[i].Days);
                AssertClose(expected.TotalHours, actual[i].Hours);
                AssertClose(expected.TotalMinutes, actual[i].Minutes);
                AssertClose(expected.TotalSeconds, actual[i].Seconds);
                AssertClose(expected.TotalMilliseconds, actual[i].Milliseconds);
                AssertClose(expected.TotalMicroseconds, actual[i].Microseconds);
                AssertClose(expected.TotalNanoseconds, actual[i].Nanoseconds);
            }
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DateTime_subtraction_totals_keep_sign_and_fraction_in_filters(bool duckLake)
        => Run(duckLake, context =>
        {
            Assert.Equal([1], Ids(context.Rows.Where(row => (row.Later - row.Earlier).TotalDays > 2.2)));
            Assert.Equal([3], Ids(context.Rows.Where(row => (row.Later - row.Earlier).TotalDays < 0)));
            Assert.Equal([1], Ids(context.Rows.Where(row => (row.Later - row.Earlier).TotalHours > 24)));

            // Crossing midnight is two hours, not one day boundary.
            Assert.Equal([2], Ids(context.Rows.Where(row => (row.Later - row.Earlier).TotalDays > 0
                && (row.Later - row.Earlier).TotalDays < 1)));
            Assert.Equal([2], Ids(context.Rows.Where(row => (row.Later - row.Earlier).TotalMinutes == 120)));

            var cutoff = new DateTime(2026, 1, 2, 12, 0, 0);
            Assert.Equal([1, 3, 4], Ids(context.Rows.Where(row => (row.Later - cutoff).TotalHours > 0)));
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nullable_DateTime_subtraction_totals_translate(bool duckLake)
        => Run(duckLake, context =>
        {
            var lifted = context.Rows.OrderBy(row => row.Id)
                .Select(row => (double?)(row.NullableLater - row.NullableEarlier)!.Value.TotalHours);
            var unwrapped = context.Rows
                .Where(row => row.NullableLater != null && row.NullableEarlier != null)
                .Where(row => (row.NullableLater!.Value - row.NullableEarlier!.Value).TotalDays < 0);

            Assert.Contains("epoch_us", lifted.ToQueryString());
            var hours = lifted.ToArray();
            AssertClose(53.52, hours[0]!.Value);
            AssertClose(2, hours[1]!.Value);
            AssertClose(-29.999930555555555, hours[2]!.Value);
            Assert.Null(hours[3]);
            Assert.Equal([3], Ids(unwrapped));
            Assert.Equal([1], Ids(context.Rows.Where(row => (row.NullableLater - row.NullableEarlier)!.Value.TotalDays > 2)));
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DateTimeOffset_subtraction_totals_measure_instants(bool duckLake)
        => Run(duckLake, context =>
        {
            var query = context.Rows.OrderBy(row => row.Id).Select(row => new
            {
                Hours = (row.LaterOffset - row.EarlierOffset).TotalHours,
                NullableSeconds = (double?)(row.NullableLaterOffset - row.EarlierOffset)!.Value.TotalSeconds
            });

            var sql = query.ToQueryString();
            var actual = query.ToArray();

            Assert.Contains("epoch_us", sql);
            for (var i = 0; i < Rows.Length; i++)
            {
                AssertClose((Rows[i].LaterOffset - Rows[i].EarlierOffset).TotalHours, actual[i].Hours);
                if (Rows[i].NullableLaterOffset is { } later)
                {
                    AssertClose((later - Rows[i].EarlierOffset).TotalSeconds, actual[i].NullableSeconds!.Value);
                }
                else
                {
                    Assert.Null(actual[i].NullableSeconds);
                }
            }

            Assert.Equal([3], Ids(context.Rows.Where(row => (row.LaterOffset - row.EarlierOffset).TotalDays < 0)));
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DateTime_fractional_add_methods_match_dotnet(bool duckLake)
        => Run(duckLake, context =>
        {
            var hours = -50.25;
            var query = context.Rows.OrderBy(row => row.Id).Select(row => new
            {
                Hours = row.Earlier.AddHours(1.5),
                NegativeHours = row.Earlier.AddHours(hours),
                Minutes = row.Earlier.AddMinutes(-90.5),
                Seconds = row.Earlier.AddSeconds(0.25),
                Milliseconds = row.Earlier.AddMilliseconds(1500.5),
                Microseconds = row.Earlier.AddMicroseconds(-3),
                Ticks = row.Earlier.AddTicks(25),
                NegativeTicks = row.Earlier.AddTicks(-25)
            });

            var sql = query.ToQueryString();
            var actual = query.ToArray();

            Assert.Contains("INTERVAL '1 hour'", sql);
            Assert.Contains("to_microseconds(divide(", sql);
            for (var i = 0; i < Rows.Length; i++)
            {
                var earlier = Rows[i].Earlier;
                Assert.Equal(earlier.AddHours(1.5), actual[i].Hours);
                Assert.Equal(earlier.AddHours(hours), actual[i].NegativeHours);
                Assert.Equal(earlier.AddMinutes(-90.5), actual[i].Minutes);
                Assert.Equal(earlier.AddSeconds(0.25), actual[i].Seconds);
                Assert.Equal(earlier.AddMilliseconds(1500.5), actual[i].Milliseconds);
                Assert.Equal(earlier.AddMicroseconds(-3), actual[i].Microseconds);

                // Ticks are truncated towards zero to DuckDB's microsecond precision.
                Assert.Equal(earlier.AddMicroseconds(2), actual[i].Ticks);
                Assert.Equal(earlier.AddMicroseconds(-2), actual[i].NegativeTicks);
            }
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DateTime_add_methods_translate_inside_filters(bool duckLake)
        => Run(duckLake, context =>
        {
            Assert.Equal([1], Ids(context.Rows.Where(row => row.Earlier.AddHours(50) < row.Later)));
            Assert.Equal([1, 2], Ids(context.Rows.Where(row => row.NullableEarlier!.Value.AddMinutes(90) < row.Later)));
            Assert.Equal([4], Ids(context.Rows.Where(row => row.Earlier.AddSeconds(-1) < row.Later
                && row.Earlier.AddSeconds(1) > row.Later)));
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DateTimeOffset_fractional_add_methods_match_dotnet(bool duckLake)
        => Run(duckLake, context =>
        {
            var query = context.Rows.OrderBy(row => row.Id).Select(row => new
            {
                Hours = row.EarlierOffset.AddHours(-1.5),
                Minutes = row.EarlierOffset.AddMinutes(30.5),
                Seconds = row.EarlierOffset.AddSeconds(-0.75),
                Milliseconds = row.EarlierOffset.AddMilliseconds(2.5),
                Microseconds = row.EarlierOffset.AddMicroseconds(7),
                Ticks = row.EarlierOffset.AddTicks(TimeSpan.TicksPerHour + 9)
            });

            var actual = query.ToArray();

            for (var i = 0; i < Rows.Length; i++)
            {
                var earlier = Rows[i].EarlierOffset;
                Assert.Equal(earlier.AddHours(-1.5), actual[i].Hours);
                Assert.Equal(earlier.AddMinutes(30.5), actual[i].Minutes);
                Assert.Equal(earlier.AddSeconds(-0.75), actual[i].Seconds);
                Assert.Equal(earlier.AddMilliseconds(2.5), actual[i].Milliseconds);
                Assert.Equal(earlier.AddMicroseconds(7), actual[i].Microseconds);
                Assert.Equal(earlier.AddHours(1), actual[i].Ticks);
            }

            Assert.Equal([1], Ids(context.Rows.Where(row => row.EarlierOffset.AddHours(50) < row.LaterOffset)));
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DayOfWeek_translates_for_DateTime_and_DateTimeOffset(bool duckLake)
        => Run(duckLake, context =>
        {
            var query = context.Rows.OrderBy(row => row.Id).Select(row => new
            {
                row.Later.DayOfWeek,
                NullableDayOfWeek = (DayOfWeek?)row.NullableLater!.Value.DayOfWeek,
                OffsetDayOfWeek = row.LaterOffset.DayOfWeek
            });

            var sql = query.ToQueryString();
            var actual = query.ToArray();

            Assert.Contains("dayofweek", sql);
            for (var i = 0; i < Rows.Length; i++)
            {
                Assert.Equal(Rows[i].Later.DayOfWeek, actual[i].DayOfWeek);
                Assert.Equal(Rows[i].NullableLater?.DayOfWeek, actual[i].NullableDayOfWeek);

                // TIMESTAMPTZ materialises at +00:00, so the weekday is the UTC one.
                Assert.Equal(Rows[i].LaterOffset.UtcDateTime.DayOfWeek, actual[i].OffsetDayOfWeek);
            }

            Assert.Equal([1], Ids(context.Rows.Where(row => row.Later.DayOfWeek == DayOfWeek.Saturday)));
            Assert.Equal([3, 4], Ids(context.Rows.Where(row => row.Earlier.DayOfWeek == DayOfWeek.Monday
                || row.Earlier.DayOfWeek == DayOfWeek.Sunday)));
            Assert.Equal([2], Ids(context.Rows.Where(row => row.EarlierOffset.DayOfWeek == DayOfWeek.Thursday
                && row.LaterOffset.DayOfWeek == DayOfWeek.Thursday)));
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DateTimeOffset_parts_and_adds_do_not_depend_on_the_session_TimeZone(bool duckLake)
        => Run(duckLake, context =>
        {
            context.Database.OpenConnection();
            try
            {
                // FridayEvening is 2026-01-02 20:00Z: Friday in UTC, Saturday in Tokyo. BeforeSummerTime plus 1.5 days
                // crosses the start of British Summer Time, where a calendar day is 23 hours long.
                var row = Rows[0];

                context.Database.ExecuteSqlRaw("SET TimeZone = 'Asia/Tokyo'");
                Assert.Equal(
                    DayOfWeek.Friday,
                    context.Rows.Where(r => r.Id == 1).Select(r => r.FridayEvening.DayOfWeek).Single());
                Assert.Equal([1], Ids(context.Rows.Where(r => r.Id == 1 && r.FridayEvening.DayOfWeek == DayOfWeek.Friday)));

                // YearEnd is 2026-12-31 20:15:45Z, which in Kolkata (+05:30) is 2027-01-01 01:45:45.
                context.Database.ExecuteSqlRaw("SET TimeZone = 'Asia/Kolkata'");
                var parts = context.Rows.Where(r => r.Id == 1).Select(r => new
                {
                    r.YearEnd.Year,
                    r.YearEnd.Month,
                    r.YearEnd.Day,
                    r.YearEnd.Hour,
                    r.YearEnd.Minute,
                    r.YearEnd.Second
                }).Single();
                Assert.Equal(
                    (row.YearEnd.Year, row.YearEnd.Month, row.YearEnd.Day, row.YearEnd.Hour, row.YearEnd.Minute, row.YearEnd.Second),
                    (parts.Year, parts.Month, parts.Day, parts.Hour, parts.Minute, parts.Second));
                Assert.Equal([1], Ids(context.Rows.Where(r => r.Id == 1 && r.YearEnd.Year == 2026 && r.YearEnd.Day == 31
                    && r.YearEnd.Hour == 20 && r.YearEnd.Minute == 15)));

                // In London, BeforeSummerTime plus a month or a year lands in British Summer Time.
                context.Database.ExecuteSqlRaw("SET TimeZone = 'Europe/London'");
                Assert.Equal(
                    row.BeforeSummerTime.AddMonths(1),
                    context.Rows.Where(r => r.Id == 1).Select(r => r.BeforeSummerTime.AddMonths(1)).Single());
                Assert.Equal(
                    row.BeforeSummerTime.AddYears(1),
                    context.Rows.Where(r => r.Id == 1).Select(r => r.BeforeSummerTime.AddYears(1)).Single());
                Assert.Equal(
                    row.BeforeSummerTime.AddDays(1.5),
                    context.Rows.Where(r => r.Id == 1).Select(r => r.BeforeSummerTime.AddDays(1.5)).Single());
                Assert.Equal(
                    row.BeforeSummerTime.AddDays(2),
                    context.Rows.Where(r => r.Id == 1).Select(r => r.BeforeSummerTime.AddDays(2)).Single());
            }
            finally
            {
                context.Database.CloseConnection();
            }
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Subtraction_totals_over_kind_setting_converters_translate(bool duckLake)
        => Run(duckLake, context =>
        {
            var query = context.Rows.Where(row => (row.UtcLater - row.UtcEarlier).TotalHours > 24);

            Assert.Contains("epoch_us", query.ToQueryString());
            Assert.Equal([1], Ids(query));
        });

    [ConditionalFact]
    public void Subtraction_totals_over_converted_timestamps_are_not_translated()
    {
        using var context = new ConvertedContext(FileOptions<ConvertedContext>());
        context.Database.EnsureCreated();

        // The columns hold ticks as BIGINT, so epoch_us cannot measure them; translation must fail, not miscompute.
        var exception = Assert.Throws<InvalidOperationException>(() => context.Rows
            .Where(row => (row.Later - row.Earlier).TotalDays > 1)
            .ToArray());

        Assert.Contains("could not be translated", exception.Message);
    }

    private static int[] Ids(IQueryable<TemporalRow> query)
        => query.OrderBy(row => row.Id).Select(row => row.Id).ToArray();

    private static void AssertClose(double expected, double actual)
        => Assert.True(
            Math.Abs(expected - actual) <= Math.Max(1e-9, Math.Abs(expected) * 1e-12),
            $"Expected {expected:R} but was {actual:R}.");

    private void Run(bool duckLake, Action<TemporalContext> test)
    {
        var root = Path.Combine(Path.GetTempPath(), $"temporal_translation_{Guid.NewGuid():N}");
        try
        {
            DbContextOptions<TemporalContext> options;
            if (duckLake)
            {
                var dataPath = Path.Combine(root, "data");
                Directory.CreateDirectory(dataPath);
                options = new DbContextOptionsBuilder<TemporalContext>()
                    .UseDuckLake(Path.Combine(root, "catalog.ducklake"), duckLake => duckLake.DataPath(dataPath))
                    .EnableServiceProviderCaching(false)
                    .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                    .Options;
            }
            else
            {
                options = FileOptions<TemporalContext>();
            }

            using var context = new TemporalContext(options);
            context.Database.EnsureCreated();
            context.Rows.AddRange(Rows.Select(row => row.Clone()));
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

    private sealed class TemporalContext(DbContextOptions<TemporalContext> options) : DbContext(options)
    {
        public DbSet<TemporalRow> Rows => Set<TemporalRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<TemporalRow>(entity =>
            {
                entity.Property(row => row.Id).ValueGeneratedNever();

                // A DateTime-to-DateTime converter still stores TIMESTAMP, so subtraction totals stay translatable.
                entity.Property(row => row.UtcEarlier).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
                entity.Property(row => row.UtcLater).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            });
    }

    private sealed class ConvertedContext(DbContextOptions<ConvertedContext> options) : DbContext(options)
    {
        public DbSet<ConvertedRow> Rows => Set<ConvertedRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ConvertedRow>(entity =>
            {
                entity.Property(row => row.Earlier).HasConversion<long>();
                entity.Property(row => row.Later).HasConversion<long>();
            });
    }

    private sealed class ConvertedRow
    {
        public int Id { get; set; }

        public DateTime Earlier { get; set; }

        public DateTime Later { get; set; }
    }

    private sealed class TemporalRow
    {
        public int Id { get; set; }

        public DateTime Earlier { get; set; }

        public DateTime Later { get; set; }

        public DateTime? NullableEarlier { get; set; }

        public DateTime? NullableLater { get; set; }

        public DateTimeOffset EarlierOffset { get; set; }

        public DateTimeOffset LaterOffset { get; set; }

        public DateTimeOffset? NullableLaterOffset { get; set; }

        public DateTime UtcEarlier { get; set; }

        public DateTime UtcLater { get; set; }

        public DateTimeOffset FridayEvening { get; set; } = new(2026, 1, 2, 20, 0, 0, TimeSpan.Zero);

        public DateTimeOffset BeforeSummerTime { get; set; } = new(2026, 3, 28, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset YearEnd { get; set; } = new(2026, 12, 31, 20, 15, 45, TimeSpan.Zero);

        public TemporalRow Clone()
        {
            var clone = (TemporalRow)MemberwiseClone();
            clone.UtcEarlier = Earlier;
            clone.UtcLater = Later;
            return clone;
        }
    }
}