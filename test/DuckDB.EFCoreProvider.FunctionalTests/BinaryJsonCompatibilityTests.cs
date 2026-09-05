using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class BinaryJsonCompatibilityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("changed")]
    public async Task Unsupported_owned_JSON_partial_updates_leave_the_document_intact(string? value)
    {
        using var connection = new DuckDBConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new OwnedJsonContext(
            new DbContextOptionsBuilder<OwnedJsonContext>().UseDuckDB(connection).EnableServiceProviderCaching(false)
                .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)).Options);
        await context.Database.EnsureCreatedAsync();
        context.Add(new OwnedRow { Id = 1 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var row = await context.Set<OwnedRow>().SingleAsync();
        row.Payload.Name = value;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("partial updates of owned JSON", exception.Message);

        context.ChangeTracker.Clear();
        row = await context.Set<OwnedRow>().SingleAsync();
        Assert.Equal("original", row.Payload.Name);
        Assert.Equal(7, row.Payload.Number);
    }

    [Fact]
    public async Task Binary_values_in_complex_JSON_preserve_null_empty_and_updated_values()
    {
        using var connection = new DuckDBConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new JsonContext(new DbContextOptionsBuilder<JsonContext>().UseDuckDB(connection).EnableServiceProviderCaching(false)
                .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)).Options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(
            new Row { Id = 1, Payload = new Payload { Bytes = null } },
            new Row { Id = 2, Payload = new Payload { Bytes = [] } },
            new Row { Id = 3, Payload = new Payload { Bytes = [1, 2, 255] } });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var rows = await context.Set<Row>().OrderBy(row => row.Id).ToListAsync();
        Assert.Null(rows[0].Payload.Bytes);
        Assert.Empty(rows[1].Payload.Bytes!);
        Assert.Equal(new byte[] { 1, 2, 255 }, rows[2].Payload.Bytes);

        rows[2].Payload.Bytes = [4, 5];
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(new byte[] { 4, 5 }, (await context.Set<Row>().SingleAsync(row => row.Id == 3)).Payload.Bytes);
    }

    private sealed class JsonContext(DbContextOptions<JsonContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Row>().Property(row => row.Id).ValueGeneratedNever();
            modelBuilder.Entity<Row>().ComplexProperty(row => row.Payload).ToJson();
        }
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public Payload Payload { get; set; } = new();
    }

    private sealed class Payload
    {
        public byte[]? Bytes { get; set; }
    }

    private sealed class OwnedJsonContext(DbContextOptions<OwnedJsonContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OwnedRow>().Property(row => row.Id).ValueGeneratedNever();
            modelBuilder.Entity<OwnedRow>().OwnsOne(row => row.Payload).ToJson();
        }
    }

    private sealed class OwnedRow
    {
        public int Id { get; set; }
        public OwnedPayload Payload { get; set; } = new();
    }

    private sealed class OwnedPayload
    {
        public string? Name { get; set; } = "original";
        public int Number { get; set; } = 7;
    }
}