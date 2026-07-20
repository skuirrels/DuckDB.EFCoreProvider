using DuckDB.EFCoreProvider.Extensions;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Regression coverage for DuckDB arithmetic whose physical SQL result type differs from the CLR type
///     represented by EF Core's SQL tree.
/// </summary>
public class DecimalAndDateArithmeticTranslationTests : DuckDBTestBase
{
    private ArithmeticContext CreateContext()
        => new(FileOptions<ArithmeticContext>());

    [ConditionalFact]
    public void Decimal_division_preserves_intermediate_precision()
    {
        Seed();
        using var context = CreateContext();

        var query = context.JobContainers
            .Where(container => container.Id == 3)
            .Select(container => new
            {
                Quotient = container.WeightCapacity / container.VolumeCapacity,
                Percentage = container.WeightCapacity / container.VolumeCapacity * 100m,
                ExpandedRange = container.SmallNumerator / container.SmallDenominator
            });

        var sql = query.ToQueryString();
        var result = query.Single();

        Assert.Equal(0.333m, decimal.Round(result.Quotient, 3));
        Assert.Equal(33.333m, decimal.Round(result.Percentage, 3));
        Assert.Equal(99999m, result.ExpandedRange);
        Assert.Contains("CAST(", sql);
        Assert.Contains("AS DECIMAL(38,20)", sql);
        Assert.Contains("AS DECIMAL(13,8)", sql);
    }

    [ConditionalFact]
    public void Correlated_decimal_sum_division_inside_case_materializes_as_decimal()
    {
        Seed();
        using var context = CreateContext();

        var query = context.JobContainers
            .Where(container => container.Id <= 2)
            .OrderBy(container => container.Id)
            .Select(container => new
            {
                container.Id,
                WeightUtilisation = container.WeightCapacity > 0
                    ? context.JobItems
                        .Where(item => item.JobContainerId == container.Id)
                        .Sum(item => item.Weight) / container.WeightCapacity * 100
                    : 0,
                VolumeUtilisation = container.VolumeCapacity > 0
                    ? context.JobItems
                        .Where(item => item.JobContainerId == container.Id)
                        .Sum(item => item.Volume) / container.VolumeCapacity * 100
                    : 0
            });

        var sql = query.ToQueryString();
        var results = query.ToList();

        Assert.Equal(37.500m, results[0].WeightUtilisation);
        Assert.Equal(30.000m, results[0].VolumeUtilisation);
        Assert.Equal(0m, results[1].WeightUtilisation);
        Assert.Equal(0m, results[1].VolumeUtilisation);
        Assert.Contains("CASE", sql);
        Assert.Contains("SELECT", sql);
        Assert.Contains("AS DECIMAL(38,20)", sql);
    }

    [ConditionalFact]
    public void Correlated_decimal_day_count_supports_DateTime_AddDays()
    {
        Seed();
        using var context = CreateContext();

        var query = context.Bookings.Select(booking => booking.CargoReadyDate.AddDays(
            (double)context.KpiDataItems
                .Where(kpi => kpi.BookingId == booking.Id)
                .Select(kpi => kpi.AgreedTransitHours / 24 + (kpi.Template == null ? 0 : kpi.Template.BufferDays))
                .FirstOrDefault()));

        var sql = query.ToQueryString();
        var result = query.Single();

        Assert.Equal(new DateTime(2026, 6, 3, 9, 0, 0), result);
        Assert.Contains("date_add", sql);
        Assert.Contains("INTERVAL '1 day'", sql);
        Assert.DoesNotContain("to_days", sql);
    }

    [ConditionalFact]
    public void DateTime_AddDays_preserves_fractional_days()
    {
        Seed();
        using var context = CreateContext();

        var query = context.Bookings.Select(booking => booking.CargoReadyDate.AddDays(1.5));

        var sql = query.ToQueryString();
        var result = query.Single();

        Assert.Equal(new DateTime(2026, 6, 2, 20, 0, 0), result);
        Assert.Contains("INTERVAL '1 day'", sql);
        Assert.DoesNotContain("to_days", sql);
    }

    [ConditionalFact]
    public void DateTimeOffset_AddDays_preserves_fractional_days()
    {
        Seed();
        using var context = CreateContext();

        var query = context.Bookings.Select(booking => booking.CargoReadyOffset.AddDays(1.5));

        var sql = query.ToQueryString();
        var result = query.Single();

        Assert.Equal(new DateTimeOffset(2026, 6, 2, 20, 0, 0, TimeSpan.Zero), result);
        Assert.Contains("INTERVAL '1 day'", sql);
        Assert.DoesNotContain("to_days", sql);
    }

    [ConditionalFact]
    public void DateOnly_date_add_methods_materialize_as_DateOnly()
    {
        Seed();
        using var context = CreateContext();

        var query = context.Bookings.Select(booking => new
        {
            Days = booking.CargoReadyOn.AddDays(2),
            Months = booking.CargoReadyOn.AddMonths(1),
            Years = booking.CargoReadyOn.AddYears(1)
        });

        var sql = query.ToQueryString();
        var result = query.Single();

        Assert.Equal(new DateOnly(2026, 6, 3), result.Days);
        Assert.Equal(new DateOnly(2026, 7, 1), result.Months);
        Assert.Equal(new DateOnly(2027, 6, 1), result.Years);
        Assert.Contains("to_days", sql);
        Assert.Contains("to_months", sql);
        Assert.Contains("to_years", sql);
        Assert.Contains("AS DATE", sql);
        Assert.DoesNotContain("INTERVAL '1 day'", sql);
    }

    private void Seed()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();

        context.JobContainers.AddRange(
            new JobContainer { Id = 1, WeightCapacity = 200m, VolumeCapacity = 400m },
            new JobContainer { Id = 2, WeightCapacity = 0m, VolumeCapacity = 0m },
            new JobContainer
            {
                Id = 3,
                WeightCapacity = 1m,
                VolumeCapacity = 3m,
                SmallNumerator = 999.99m,
                SmallDenominator = 0.01m
            });
        context.JobItems.AddRange(
            new JobItem { Id = 1, JobContainerId = 1, Weight = 50m, Volume = 60m },
            new JobItem { Id = 2, JobContainerId = 1, Weight = 25m, Volume = 60m });

        context.Bookings.Add(new Booking
        {
            Id = 1,
            CargoReadyDate = new DateTime(2026, 6, 1, 8, 0, 0),
            CargoReadyOffset = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            CargoReadyOn = new DateOnly(2026, 6, 1)
        });
        context.KpiTemplates.Add(new KpiTemplate { Id = 1, BufferDays = 2 });
        context.KpiDataItems.Add(new KpiDataItem
        {
            Id = 1,
            BookingId = 1,
            AgreedTransitHours = 1m,
            TemplateId = 1
        });

        context.SaveChanges();
    }

    private sealed class ArithmeticContext(DbContextOptions<ArithmeticContext> options) : DbContext(options)
    {
        public DbSet<JobContainer> JobContainers => Set<JobContainer>();
        public DbSet<JobItem> JobItems => Set<JobItem>();
        public DbSet<Booking> Bookings => Set<Booking>();
        public DbSet<KpiDataItem> KpiDataItems => Set<KpiDataItem>();
        public DbSet<KpiTemplate> KpiTemplates => Set<KpiTemplate>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<JobContainer>(entity =>
            {
                entity.Property(container => container.Id).ValueGeneratedNever();
                entity.Property(container => container.WeightCapacity).HasPrecision(18, 3);
                entity.Property(container => container.VolumeCapacity).HasPrecision(18, 3);
                entity.Property(container => container.SmallNumerator).HasPrecision(5, 2);
                entity.Property(container => container.SmallDenominator).HasPrecision(5, 2);
            });

            modelBuilder.Entity<JobItem>(entity =>
            {
                entity.Property(item => item.Id).ValueGeneratedNever();
                entity.Property(item => item.Weight).HasPrecision(18, 3);
                entity.Property(item => item.Volume).HasPrecision(18, 3);
            });

            modelBuilder.Entity<Booking>().Property(booking => booking.Id).ValueGeneratedNever();
            modelBuilder.Entity<KpiDataItem>(entity =>
            {
                entity.Property(kpi => kpi.Id).ValueGeneratedNever();
                entity.Property(kpi => kpi.AgreedTransitHours).HasPrecision(18, 3);
            });
            modelBuilder.Entity<KpiTemplate>().Property(template => template.Id).ValueGeneratedNever();
        }
    }

    private sealed class JobContainer
    {
        public int Id { get; set; }
        public decimal WeightCapacity { get; set; }
        public decimal VolumeCapacity { get; set; }
        public decimal SmallNumerator { get; set; }
        public decimal SmallDenominator { get; set; }
    }

    private sealed class JobItem
    {
        public int Id { get; set; }
        public int JobContainerId { get; set; }
        public decimal Weight { get; set; }
        public decimal Volume { get; set; }
    }

    private sealed class Booking
    {
        public int Id { get; set; }
        public DateTime CargoReadyDate { get; set; }
        public DateTimeOffset CargoReadyOffset { get; set; }
        public DateOnly CargoReadyOn { get; set; }
    }

    private sealed class KpiDataItem
    {
        public int Id { get; set; }
        public int BookingId { get; set; }
        public decimal AgreedTransitHours { get; set; }
        public int? TemplateId { get; set; }
        public KpiTemplate? Template { get; set; }
    }

    private sealed class KpiTemplate
    {
        public int Id { get; set; }
        public int BufferDays { get; set; }
    }
}