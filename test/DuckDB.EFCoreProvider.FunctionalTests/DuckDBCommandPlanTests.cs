using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Collections.ObjectModel;
using System.Data;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class DuckDBCommandPlanTests : DuckDBTestBase
{
    [ConditionalFact]
    public void Query_plan_captures_provider_command_without_opening_the_connection()
    {
        using var context = new PlanContext(FileOptions<PlanContext>());
        var threshold = 3;

        var plan = context.Database.GetDuckDBCommandPlan(
            context.Entities.Where(entity => entity.Id > threshold).Select(entity => entity.Name));

        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Contains("SELECT e.\"Name\"", plan.CommandText);
        Assert.Contains("WHERE e.\"Id\" > $", plan.CommandText);
        var parameter = Assert.Single(plan.Parameters);
        Assert.Equal(typeof(int), parameter.ClrType);
        Assert.Equal(DbType.Int32, parameter.DbType);
        Assert.Equal(3, parameter.Value);
        Assert.DoesNotContain('$', parameter.Name);
    }

    [ConditionalFact]
    public void Query_plan_preserves_faceted_temporal_and_converted_type_mappings()
    {
        using var context = new PlanContext(FileOptions<PlanContext>());
        var amount = 12.34m;
        var capturedAt = new DateTime(2026, 8, 2, 12, 34, 56, DateTimeKind.Utc);
        var code = new PlanCode("alpha");

        var plan = context.Database.GetDuckDBCommandPlan(
            context.Entities.Where(entity =>
                entity.Amount > amount
                && entity.CapturedAt < capturedAt
                && entity.Code == code));

        var amountParameter = Assert.Single(plan.Parameters, parameter => parameter.ClrType == typeof(decimal));
        Assert.Equal("DECIMAL(12,2)", amountParameter.StoreType);

        var timestampParameter = Assert.Single(plan.Parameters, parameter => parameter.ClrType == typeof(DateTime));
        Assert.Equal("TIMESTAMP_NS", timestampParameter.StoreType);

        var convertedParameter = Assert.Single(plan.Parameters, parameter => parameter.ClrType == typeof(PlanCode));
        Assert.Equal("VARCHAR", convertedParameter.StoreType);
        Assert.Equal("alpha", convertedParameter.Value);
    }

    [ConditionalFact]
    public void Query_plan_owns_mutable_collection_parameter_values()
    {
        var ids = new List<int> { 1, 2 };
        var parameter = new DuckDBCommandPlanParameter(
            "ids",
            typeof(List<int>),
            typeof(DuckDB.NET.Data.DuckDBParameter),
            DbType.Object,
            false,
            ids,
            "INTEGER[]",
            "DuckDBArrayTypeMapping");
        var plan = new DuckDBCommandPlan("SELECT * FROM events WHERE id = ANY($ids)", [parameter]);

        ids.Add(3);
        var firstRead = Assert.IsType<List<int>>(plan.Parameters[0].Value);
        Assert.Equal([1, 2], firstRead);

        firstRead.Add(4);
        Assert.Equal([1, 2], Assert.IsType<List<int>>(plan.Parameters[0].Value));
    }

    [ConditionalFact]
    public void Query_plan_detaches_collection_wrappers_without_default_constructors()
    {
        var source = new List<int> { 1, 2 };
        var parameter = new DuckDBCommandPlanParameter(
            "ids",
            typeof(ReadOnlyCollection<int>),
            typeof(DuckDB.NET.Data.DuckDBParameter),
            DbType.Object,
            false,
            source.AsReadOnly());

        source.Add(3);

        Assert.Equal([1, 2], Assert.IsType<List<int>>(parameter.Value));
    }

    [ConditionalFact]
    public void Identifier_generation_uses_deterministic_provider_owned_metadata()
    {
        using var context = new PlanContext(FileOptions<PlanContext>());
        var helper = context.GetService<ISqlGenerationHelper>();

        Assert.Equal("\"select\"", helper.DelimitIdentifier("select"));
        Assert.Equal("\"length\"", helper.DelimitIdentifier("length"));
    }

    [ConditionalFact]
    public void Terminal_plans_capture_count_and_any_commands_without_execution()
    {
        using var context = new PlanContext(FileOptions<PlanContext>());
        var query = context.Entities.Where(entity => entity.Id > 3);

        var count = context.Database.GetDuckDBCountCommandPlan(query);
        var any = context.Database.GetDuckDBAnyCommandPlan(query);

        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Contains("count(*)", count.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXISTS", any.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(count.Parameters);
        Assert.Empty(any.Parameters);
    }

    [ConditionalFact]
    public async Task Extracted_terminal_plan_can_be_executed_as_a_dynamic_command()
    {
        await using var context = new PlanContext(FileOptions<PlanContext>());
        await context.Database.EnsureCreatedAsync();
        context.AddRange(
            new PlanEntity { Id = 1, Name = "one", Code = new PlanCode("one") },
            new PlanEntity { Id = 2, Name = "two", Code = new PlanCode("two") });
        await context.SaveChangesAsync();

        var minimum = 1;
        var plan = context.Database.GetDuckDBCountCommandPlan(
            context.Entities.Where(entity => entity.Id >= minimum));

        var parameter = Assert.Single(plan.Parameters);
        Assert.Equal("INTEGER", parameter.StoreType);
        Assert.Equal(typeof(int), parameter.ClrType);
        Assert.Equal(1, parameter.Value);
        Assert.Contains("Int32", parameter.TypeMapping);

        await using var result = await context.Database.SqlQueryDynamicCommandAsync(plan);
        await foreach (var row in result.ReadRowsAsync())
        {
            Assert.Equal(2L, row.Span[0]);
        }
    }

    [ConditionalFact]
    public void Rejects_query_from_another_context()
    {
        using var first = new PlanContext(FileOptions<PlanContext>());
        using var second = new PlanContext(FileOptions<PlanContext>());

        var exception = Assert.Throws<ArgumentException>(
            () => first.Database.GetDuckDBCommandPlan(second.Entities));
        var terminalException = Assert.Throws<ArgumentException>(
            () => first.Database.GetDuckDBCountCommandPlan(second.Entities));

        Assert.Contains("same DbContext", exception.Message);
        Assert.Contains("same DbContext", terminalException.Message);
    }

    [ConditionalFact]
    public void Rejects_split_query_because_it_has_multiple_commands()
    {
        using var context = new PlanContext(FileOptions<PlanContext>());

        var exception = Assert.Throws<NotSupportedException>(
            () => context.Database.GetDuckDBCommandPlan(
                context.Entities.Include(entity => entity.Children).AsSplitQuery()));

        Assert.Contains("multiple commands", exception.Message);
    }

    [ConditionalFact]
    public void DuckLake_profile_uses_the_same_non_executing_extraction_contract()
    {
        var metadataPath = Path.Combine(Path.GetTempPath(), $"plan_{Guid.NewGuid():N}.ducklake");
        var options = new DbContextOptionsBuilder<PlanContext>()
            .UseDuckLake(metadataPath)
            .Options;
        using var context = new PlanContext(options);

        var plan = context.Database.GetDuckDBAnyCommandPlan(
            context.Entities.Where(entity => entity.Name == "test"));

        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Contains("EXISTS", plan.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(plan.Parameters);
        Assert.False(File.Exists(metadataPath));
    }

    private sealed class PlanContext(DbContextOptions<PlanContext> options) : DbContext(options)
    {
        public DbSet<PlanEntity> Entities => Set<PlanEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PlanEntity>(entity =>
            {
                entity.HasKey(value => value.Id);
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.Property(value => value.Amount).HasColumnType("DECIMAL(12,2)");
                entity.Property(value => value.CapturedAt).HasColumnType("TIMESTAMP_NS");
                entity.Property(value => value.Code)
                    .HasConversion(value => value.Value, value => new PlanCode(value))
                    .HasColumnType("VARCHAR");
                entity.HasMany(value => value.Children).WithOne().HasForeignKey(value => value.ParentId);
            });
            modelBuilder.Entity<PlanChild>().HasKey(value => value.Id);
        }
    }

    private sealed class PlanEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public decimal Amount { get; set; }
        public DateTime CapturedAt { get; set; }
        public PlanCode Code { get; set; }
        public List<PlanChild> Children { get; set; } = [];
    }

    private sealed class PlanChild
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
    }

    private readonly record struct PlanCode(string Value);
}