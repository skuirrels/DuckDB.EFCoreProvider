using DuckDB.EFCoreProvider.Extensions;
using System.Data;
using System.Numerics;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class DynamicQueryResultTests : DuckDBTestBase
{
    [ConditionalFact]
    public async Task Streams_unknown_shape_with_lossless_runtime_metadata()
    {
        await using var context = new DynamicContext(FileOptions<DynamicContext>());

        await using var result = await context.Database.SqlQueryDynamicRawAsync(
            """
            SELECT 7::INTEGER AS id,
                   170141183460469231731687303715884105727::HUGEINT AS huge_value,
                   MAP {'items': [1, 2, 3]} AS nested_value,
                   NULL::VARCHAR AS missing_value
            """);

        Assert.Collection(
            result.Columns,
            column => AssertColumn(column, 0, "id", "Integer", typeof(int)),
            column => AssertColumn(column, 1, "huge_value", "HugeInt", typeof(BigInteger)),
            column => AssertColumn(column, 2, "nested_value", "Map", typeof(Dictionary<string, List<int>>)),
            column => AssertColumn(column, 3, "missing_value", "Varchar", typeof(string)));

        var rows = new List<ReadOnlyMemory<object?>>();
        await foreach (var row in result.ReadRowsAsync())
        {
            rows.Add(row);
        }

        var values = Assert.Single(rows).Span;
        Assert.Equal(7, values[0]);
        Assert.Equal(BigInteger.Parse("170141183460469231731687303715884105727"), values[1]);
        var nested = Assert.IsType<Dictionary<string, List<int>>>(values[2]);
        Assert.Equal([1, 2, 3], nested["items"]);
        Assert.Null(values[3]);
    }

    [ConditionalFact]
    public async Task Parameterizes_raw_and_interpolated_values()
    {
        await using var context = new DynamicContext(FileOptions<DynamicContext>());

        await using (var raw = await context.Database.SqlQueryDynamicRawAsync(
            "SELECT {0}::INTEGER AS value",
            [42]))
        {
            await foreach (var row in raw.ReadRowsAsync())
            {
                Assert.Equal(42, row.Span[0]);
            }
        }

        var expected = "value with ' punctuation";
        await using var interpolated = await context.Database.SqlQueryDynamicAsync(
            $"SELECT {expected}::VARCHAR AS value");
        await foreach (var row in interpolated.ReadRowsAsync())
        {
            Assert.Equal(expected, row.Span[0]);
        }
    }

    [ConditionalFact]
    public async Task Owns_connection_until_result_is_disposed()
    {
        await using var context = new DynamicContext(FileOptions<DynamicContext>());
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);

        var result = await context.Database.SqlQueryDynamicRawAsync("SELECT * FROM range(2)");
        Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);

        await result.DisposeAsync();
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [ConditionalFact]
    public async Task Blob_values_remain_stable_after_the_reader_advances_and_result_is_disposed()
    {
        await using var context = new DynamicContext(FileOptions<DynamicContext>());
        var result = await context.Database.SqlQueryDynamicRawAsync(
            "SELECT 'first'::BLOB AS payload UNION ALL SELECT 'second'::BLOB");
        await using var rows = result.ReadRowsAsync().GetAsyncEnumerator();

        Assert.True(await rows.MoveNextAsync());
        var firstBlob = Assert.IsAssignableFrom<Stream>(rows.Current.Span[0]);
        Assert.True(await rows.MoveNextAsync());
        await result.DisposeAsync();

        using var buffer = new MemoryStream();
        await firstBlob.CopyToAsync(buffer);
        Assert.Equal("first", System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
    }

    [ConditionalFact]
    public async Task Blob_values_nested_in_composite_results_are_row_owned()
    {
        await using var context = new DynamicContext(FileOptions<DynamicContext>());
        var result = await context.Database.SqlQueryDynamicRawAsync(
            "SELECT MAP {'payloads': ['nested'::BLOB]} AS nested_value");
        Stream? nestedBlob = null;

        await foreach (var row in result.ReadRowsAsync())
        {
            var map = Assert.IsAssignableFrom<System.Collections.IDictionary>(row.Span[0]);
            var blobs = Assert.IsAssignableFrom<System.Collections.IList>(map["payloads"]);
            nestedBlob = Assert.IsAssignableFrom<Stream>(blobs[0]);
        }

        await result.DisposeAsync();
        using var buffer = new MemoryStream();
        await nestedBlob!.CopyToAsync(buffer);
        Assert.Equal("nested", System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
    }

    [ConditionalFact]
    public async Task Row_stream_can_be_requested_only_once()
    {
        await using var context = new DynamicContext(FileOptions<DynamicContext>());
        await using var result = await context.Database.SqlQueryDynamicRawAsync("SELECT 1");

        await foreach (var _ in result.ReadRowsAsync())
        {
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
            {
                await foreach (var _ in result.ReadRowsAsync())
                {
                }
            });

        Assert.Equal("A dynamic query result can be enumerated only once.", exception.Message);
    }

    private static void AssertColumn(
        DuckDBDynamicColumn column,
        int ordinal,
        string name,
        string duckDBTypeName,
        Type clrType)
    {
        Assert.Equal(ordinal, column.Ordinal);
        Assert.Equal(name, column.Name);
        Assert.Equal(duckDBTypeName, column.DuckDBTypeName);
        Assert.Equal(clrType, column.ClrType);
    }

    private sealed class DynamicContext(DbContextOptions<DynamicContext> options) : DbContext(options);
}
