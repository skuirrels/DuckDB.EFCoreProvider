using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DuckDB.EFCoreProvider.Tests.FakeDb;

// Before the ADO.NET provider substitutability fix, every DuckDB*TypeMapping.ConfigureParameter cast
// its DbParameter argument to the concrete DuckDBParameter type before doing anything else
// (((DuckDBParameter)parameter).ConfigureNameAndMetadata(this)). EF Core invokes ConfigureParameter on
// whatever DbParameter the active connection's DbCommand.CreateParameter() produced -- against a
// substituted ADO.NET provider (pengdows.crud.fakeDb's fakeDbConnection here, but the same is true
// for any non-DuckDB provider substitute), that cast throws InvalidCastException for every single
// mapped parameter, regardless of its .NET type. These tests prove the fix: a fake connection can
// bind a WHERE-clause parameter of each affected .NET type without that cast ever firing. No real
// DuckDB engine is involved anywhere in this file.
public class FakeDbTypeMappingTests
{
    [Fact]
    public async Task String_valued_where_clause_parameter_binds_without_casting_to_the_concrete_parameter_type()
    {
        var connection = new CapturingFakeDbConnection();
        await using var db = CreateContext(connection);

        connection.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["Id"] = 1 } });

        var name = "Ada";
        var ids = await db.Widgets.Where(w => w.Name == name).Select(w => w.Id).ToListAsync();

        Assert.Equal([1], ids);
        AssertBoundParameterValue(connection, "Ada");
    }

    [Fact]
    public async Task Guid_valued_where_clause_parameter_binds_without_casting_to_the_concrete_parameter_type()
    {
        var connection = new CapturingFakeDbConnection();
        await using var db = CreateContext(connection);

        connection.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["Id"] = 1 } });

        var token = Guid.NewGuid();
        var ids = await db.Widgets.Where(w => w.Token == token).Select(w => w.Id).ToListAsync();

        Assert.Equal([1], ids);
        AssertBoundParameterValue(connection, token);
    }

    [Fact]
    public async Task Boolean_valued_where_clause_parameter_binds_without_casting_to_the_concrete_parameter_type()
    {
        var connection = new CapturingFakeDbConnection();
        await using var db = CreateContext(connection);

        connection.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["Id"] = 1 } });

        var isActive = true;
        var ids = await db.Widgets.Where(w => w.IsActive == isActive).Select(w => w.Id).ToListAsync();

        Assert.Equal([1], ids);
        AssertBoundParameterValue(connection, true);
    }

    [Fact]
    public async Task Decimal_valued_where_clause_parameter_binds_without_casting_to_the_concrete_parameter_type()
    {
        var connection = new CapturingFakeDbConnection();
        await using var db = CreateContext(connection);

        connection.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["Id"] = 1 } });

        var amount = 12.5m;
        var ids = await db.Widgets.Where(w => w.Amount == amount).Select(w => w.Id).ToListAsync();

        Assert.Equal([1], ids);
        AssertBoundParameterValue(connection, 12.5m);
    }

    [Fact]
    public async Task DateTime_valued_where_clause_parameter_binds_without_casting_to_the_concrete_parameter_type()
    {
        var connection = new CapturingFakeDbConnection();
        await using var db = CreateContext(connection);

        connection.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["Id"] = 1 } });

        var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ids = await db.Widgets.Where(w => w.CreatedAt == createdAt).Select(w => w.Id).ToListAsync();

        Assert.Equal([1], ids);
        AssertBoundParameterValue(connection, createdAt);
    }

    [Fact]
    public async Task SaveChangesAsync_inserts_a_row_carrying_bound_parameters_of_every_affected_type()
    {
        var connection = new CapturingFakeDbConnection();
        await using var db = CreateContext(connection);

        connection.EnqueueNonQueryResult(1);
        connection.EnqueueReaderResult(Array.Empty<Dictionary<string, object?>>());

        var token = Guid.NewGuid();
        db.Add(new Widget { Id = 1, Name = "Ada", IsActive = true, Token = token, Amount = 12.5m });
        await db.SaveChangesAsync();

        var allCommands = connection.ExecutedReaderCommands.Concat(connection.ExecutedNonQueryCommands).ToList();
        var insertCommand = allCommands.Single(c => c.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(insertCommand.Parameters, p => Equals(p.Value, "Ada"));
        Assert.Contains(insertCommand.Parameters, p => Equals(p.Value, true));
        Assert.Contains(insertCommand.Parameters, p => Equals(p.Value, token));
        Assert.Contains(insertCommand.Parameters, p => Equals(p.Value, 12.5m));
    }

    private static void AssertBoundParameterValue(CapturingFakeDbConnection connection, object expectedValue)
    {
        var command = connection.ExecutedReaderCommands.Single();
        Assert.Contains(command.Parameters, p => Equals(p.Value, expectedValue));
    }

    private static WidgetContext CreateContext(CapturingFakeDbConnection connection)
    {
        var options = new DbContextOptionsBuilder<WidgetContext>()
            .UseDuckDB(connection)
            .Options;
        return new WidgetContext(options);
    }

    private sealed class Widget
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsActive { get; set; }
        public Guid Token { get; set; }
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class WidgetContext(DbContextOptions<WidgetContext> options) : DbContext(options)
    {
        public DbSet<Widget> Widgets => Set<Widget>();
    }
}
