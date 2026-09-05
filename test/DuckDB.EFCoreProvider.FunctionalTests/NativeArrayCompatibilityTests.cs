using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Collections.ObjectModel;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class NativeArrayCompatibilityTests
{
    [Fact]
    public async Task Native_arrays_preserve_null_empty_values_and_converted_elements()
    {
        using var connection = new DuckDBConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ArrayContext>().UseDuckDB(connection).EnableServiceProviderCaching(false)
                .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)).Options;
        await using var context = new ArrayContext(options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(
            new Row { Id = 1, Values = null, States = null },
            new Row { Id = 2, Values = [], States = [] },
            new Row { Id = 3, Values = [4, null, 9], States = [State.Ready, State.Done] });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var rows = await context.Rows.OrderBy(row => row.Id).ToListAsync();
        Assert.Null(rows[0].Values);
        Assert.Null(rows[0].States);
        Assert.Empty(rows[1].Values!);
        Assert.Empty(rows[1].States!);
        Assert.Equal(new int?[] { 4, null, 9 }, rows[2].Values);
        Assert.Equal(new[] { State.Ready, State.Done }, rows[2].States);
        Assert.Equal(new[] { 12, 34 }, rows[2].ReadOnlyValues);

        var projected = await context.Rows.OrderBy(row => row.Id).Select(row => row.Values).ToListAsync();
        Assert.Null(projected[0]);
        Assert.Empty(projected[1]!);
        Assert.Equal(new int?[] { 4, null, 9 }, projected[2]);
        Assert.Equal(3, await context.Rows.Where(row => row.Values!.Contains(9)).Select(row => row.Id).SingleAsync());
        Assert.Equal((uint)'é', await context.Rows.Where(row => row.Id == 3).Select(row => (uint)row.Marker).SingleAsync());

        rows[2].Values = [7];
        rows[2].States = [State.Done];
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var updated = await context.Rows.SingleAsync(row => row.Id == 3);
        Assert.Equal(new int?[] { 7 }, updated.Values);
        Assert.Equal(new[] { State.Done }, updated.States);
    }

    private sealed class ArrayContext(DbContextOptions<ArrayContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Row>().Property(row => row.Id).ValueGeneratedNever();
            modelBuilder.Entity<Row>().PrimitiveCollection(row => row.States).ElementType().HasConversion<string>();
        }
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public int?[]? Values { get; set; }
        public State[]? States { get; set; }
        public ReadOnlyCollection<int> ReadOnlyValues { get; set; } = new([12, 34]);
        public char Marker { get; set; } = 'é';
    }

    private enum State { Ready, Done }
}