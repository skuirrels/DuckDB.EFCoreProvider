using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     End-to-end coverage for MAP columns mapped to dictionaries and nested LIST columns mapped to jagged arrays
///     and lists of lists, on both native DuckDB and DuckLake.
/// </summary>
public sealed class MapAndNestedListTests : DuckDBTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dictionaries_round_trip_through_SaveChanges(bool duckLake)
    {
        await using var store = Store.Create(duckLake, DbPath);
        await using (var context = store.Create())
        {
            await context.Database.EnsureCreatedAsync();
            context.Add(new Document
            {
                Id = 1,
                Tags = new() { ["colour"] = "blue", ["quote's"] = "a=b, {c}", ["blank"] = "", ["NULL"] = null },
                Counts = new Dictionary<int, long> { [1] = 10, [-2] = long.MaxValue },
                Scores = new Dictionary<string, double?> { ["pi"] = Math.PI, ["missing"] = null, ["nan"] = double.NaN },
                Settings = new Dictionary<Level, bool> { [Level.High] = true, [Level.Low] = false },
                Dates = new Dictionary<Guid, DateTime> { [KnownGuid] = new DateTime(2024, 2, 29, 13, 14, 15, 123) },
                Prices = new Dictionary<DateOnly, decimal> { [new DateOnly(2024, 1, 2)] = 12.34m },
                Matrix = [[1, 2], [], [3]],
                Grid = [["a", null], ["b"]]
            });
            await context.SaveChangesAsync();
        }

        await using (var context = store.Create())
        {
            var document = await context.Documents.SingleAsync();
            Assert.Equal(
                new Dictionary<string, string?> { ["colour"] = "blue", ["quote's"] = "a=b, {c}", ["blank"] = "", ["NULL"] = null },
                document.Tags);
            Assert.Equal(new Dictionary<int, long> { [1] = 10, [-2] = long.MaxValue }, document.Counts);
            Assert.Equal(Math.PI, document.Scores!["pi"]);
            Assert.Null(document.Scores["missing"]);
            Assert.True(double.IsNaN(document.Scores["nan"]!.Value));
            Assert.Equal(new Dictionary<Level, bool> { [Level.High] = true, [Level.Low] = false }, document.Settings);
            Assert.Equal(new DateTime(2024, 2, 29, 13, 14, 15, 123), document.Dates![KnownGuid]);
            Assert.Equal(12.34m, document.Prices![new DateOnly(2024, 1, 2)]);
            Assert.Equal([[1, 2], [], [3]], document.Matrix);
            Assert.Equal([["a", null], ["b"]], document.Grid);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task In_place_edits_are_detected_and_saved(bool duckLake)
    {
        await using var store = Store.Create(duckLake, DbPath);
        await using (var context = store.Create())
        {
            await context.Database.EnsureCreatedAsync();
            context.Add(new Document { Id = 1, Tags = new() { ["a"] = "1" }, Matrix = [[1]], Grid = [["x"]] });
            await context.SaveChangesAsync();
        }

        await using (var context = store.Create())
        {
            var document = await context.Documents.SingleAsync();
            document.Tags!["b"] = "2";
            document.Tags.Remove("a");
            document.Matrix![0][0] = 9;
            document.Grid![0].Add("y");
            Assert.Equal(1, await context.SaveChangesAsync());
        }

        await using (var context = store.Create())
        {
            var document = await context.Documents.SingleAsync();
            Assert.Equal(new Dictionary<string, string?> { ["b"] = "2" }, document.Tags);
            Assert.Equal([[9]], document.Matrix);
            Assert.Equal([["x", "y"]], document.Grid);

            // An unchanged reload produces no update.
            Assert.Equal(0, await context.SaveChangesAsync());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nulls_and_empty_values_round_trip(bool duckLake)
    {
        await using var store = Store.Create(duckLake, DbPath);
        await using (var context = store.Create())
        {
            await context.Database.EnsureCreatedAsync();
            context.AddRange(
                new Document { Id = 1 },
                new Document { Id = 2, Tags = [], Counts = new Dictionary<int, long>(), Matrix = [], Grid = [] });
            await context.SaveChangesAsync();
        }

        await using var verify = store.Create();
        var documents = await verify.Documents.OrderBy(document => document.Id).ToListAsync();
        Assert.Null(documents[0].Tags);
        Assert.Null(documents[0].Matrix);
        Assert.Empty(documents[1].Tags!);
        Assert.Empty(documents[1].Counts!);
        Assert.Empty(documents[1].Matrix!);
        Assert.Empty(documents[1].Grid!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Basic_queries_translate_to_native_map_and_list_functions(bool duckLake)
    {
        await using var store = Store.Create(duckLake, DbPath);
        await using (var context = store.Create())
        {
            await context.Database.EnsureCreatedAsync();
            context.AddRange(
                new Document { Id = 1, Tags = new() { ["colour"] = "blue", ["size"] = "L" }, Matrix = [[1, 2], [3]] },
                new Document { Id = 2, Tags = new() { ["colour"] = "red" }, Matrix = [[4]] },
                new Document { Id = 3 });
            await context.SaveChangesAsync();
        }

        await using var query = store.Create();
        Assert.Equal([1], await query.Documents.Where(d => d.Tags!["colour"] == "blue").Select(d => d.Id).ToListAsync());
        Assert.Equal([1], await query.Documents.Where(d => d.Tags!.ContainsKey("size")).Select(d => d.Id).ToListAsync());
        Assert.Equal(
            [2, 1],
            await query.Documents.Where(d => d.Tags != null).OrderBy(d => d.Id).Select(d => d.Tags!.Count).ToListAsync());

        var key = "colour";
        Assert.Equal(
            ["blue", "red"],
            await query.Documents.Where(d => d.Tags != null).OrderBy(d => d.Id).Select(d => d.Tags![key]).ToListAsync());

        Assert.Equal(
            [2, 1],
            await query.Documents.Where(d => d.Matrix != null).OrderBy(d => d.Id).Select(d => d.Matrix!.Length).ToListAsync());
        var inner = await query.Documents.Where(d => d.Id == 1).Select(d => d.Matrix![1]).SingleAsync();
        Assert.Equal([3], inner);
        Assert.Equal([1], await query.Documents.Where(d => d.Matrix![0].Contains(2)).Select(d => d.Id).ToListAsync());
        Assert.Equal([3], await query.Documents.Where(d => d.Id == 1).Select(d => d.Matrix![1][0]).ToListAsync());

        var tags = new Dictionary<string, string?> { ["colour"] = "red" };
        Assert.Equal([2], await query.Documents.Where(d => d.Tags == tags).Select(d => d.Id).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queries_over_lists_of_lists_and_converted_keys(bool duckLake)
    {
        await using var store = Store.Create(duckLake, DbPath);
        await using (var context = store.Create())
        {
            await context.Database.EnsureCreatedAsync();
            context.AddRange(
                new Document { Id = 1, Grid = [["a", "b"], ["c"]], Settings = new Dictionary<Level, bool> { [Level.High] = true } },
                new Document { Id = 2, Grid = [["d"]], Settings = new Dictionary<Level, bool> { [Level.Low] = true } });
            await context.SaveChangesAsync();
        }

        await using var query = store.Create();
        Assert.Equal(["b"], await query.Documents.Where(d => d.Id == 1).Select(d => d.Grid![0][1]).ToListAsync());
        Assert.Equal([1], await query.Documents.Where(d => d.Grid![1].Contains("c")).Select(d => d.Id).ToListAsync());
        Assert.Equal([2], await query.Documents.Where(d => d.Grid!.ElementAt(0)[0] == "d").Select(d => d.Id).ToListAsync());

        var level = Level.High;
        Assert.Equal([1], await query.Documents.Where(d => d.Settings!.ContainsKey(level)).Select(d => d.Id).ToListAsync());
        Assert.Equal([true], await query.Documents.Where(d => d.Id == 2).Select(d => d.Settings![Level.Low]).ToListAsync());
    }

    [Fact]
    public void Queries_render_native_map_and_list_sql()
    {
        using var context = new Store(false, DbPath).Create();

        var sql = context.Documents
            .Where(d => d.Tags!["colour"] == "blue" && d.Tags.ContainsKey("size") && d.Tags.Count > 1)
            .Select(d => new { Cell = d.Matrix![1][0], Has = d.Matrix[0].Contains(2) })
            .ToQueryString();

        Assert.Contains("map_extract_value(d.\"Tags\", 'colour')", sql, StringComparison.Ordinal);
        Assert.Contains("map_contains(d.\"Tags\", 'size')", sql, StringComparison.Ordinal);
        Assert.Contains("CAST(cardinality(d.\"Tags\") AS INTEGER)", sql, StringComparison.Ordinal);
        Assert.Contains("d.\"Matrix\"[2][1]", sql, StringComparison.Ordinal);
        Assert.Contains("array_contains(d.\"Matrix\"[1], 2)", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migrations_create_native_map_and_nested_list_columns(bool duckLake)
    {
        await using var store = Store.Create(duckLake, DbPath);
        await using var context = store.Create();
        await context.Database.EnsureCreatedAsync();

        var columns = await context.Database
            .SqlQueryRaw<ColumnType>(
                "SELECT column_name AS \"Name\", data_type AS \"Type\" FROM information_schema.columns "
                + "WHERE table_name = 'Documents' ORDER BY ordinal_position")
            .ToDictionaryAsync(column => column.Name, column => column.Type);

        Assert.Equal("MAP(VARCHAR, VARCHAR)", columns["Tags"]);
        Assert.Equal("MAP(INTEGER, BIGINT)", columns["Counts"]);
        Assert.Equal("MAP(INTEGER, BOOLEAN)", columns["Settings"]);
        Assert.Equal("INTEGER[][]", columns["Matrix"]);
        Assert.Equal("VARCHAR[][]", columns["Grid"]);

        var sql = context.GetService<IMigrationsSqlGenerator>()
            .Generate(context.GetService<IMigrationsModelDiffer>().GetDifferences(null, context.GetService<IDesignTimeModel>().Model.GetRelationalModel()))
            .Single(command => command.CommandText.Contains("CREATE TABLE", StringComparison.Ordinal))
            .CommandText;
        Assert.Contains("\"Tags\" MAP(VARCHAR, VARCHAR)", sql, StringComparison.Ordinal);
        Assert.Contains("\"Matrix\" INTEGER[][]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Literals_render_typed_map_and_nested_list_constructors()
    {
        using var context = new Store(false, DbPath).Create();
        var entityType = context.Model.FindEntityType(typeof(Document))!;

        var tags = entityType.FindProperty(nameof(Document.Tags))!.GetRelationalTypeMapping();
        Assert.Equal(
            "MAP {'a': 'it''s', 'b': NULL}::MAP(VARCHAR, VARCHAR)",
            tags.GenerateSqlLiteral(new Dictionary<string, string?> { ["a"] = "it's", ["b"] = null }));
        Assert.Equal("MAP {}::MAP(VARCHAR, VARCHAR)", tags.GenerateSqlLiteral(new Dictionary<string, string?>()));

        var settings = entityType.FindProperty(nameof(Document.Settings))!.GetRelationalTypeMapping();
        Assert.Equal(
            "MAP {2: true}::MAP(INTEGER, BOOLEAN)",
            settings.GenerateSqlLiteral(new Dictionary<Level, bool> { [Level.High] = true }));

        var matrix = entityType.FindProperty(nameof(Document.Matrix))!.GetRelationalTypeMapping();
        Assert.Equal(
            "[[1, 2]::INTEGER[], []::INTEGER[]]::INTEGER[][]",
            matrix.GenerateSqlLiteral(new[] { new[] { 1, 2 }, Array.Empty<int>() }));
    }

    [Fact]
    public void Map_parameters_bind_provider_dictionaries()
    {
        using var context = new Store(false, DbPath).Create();
        var entityType = context.Model.FindEntityType(typeof(Document))!;
        using var command = context.Database.GetDbConnection().CreateCommand();

        // Without element converters the dictionary is passed through unchanged.
        var tags = new Dictionary<string, string?> { ["a'b\\c"] = "x=y, z", ["n"] = null };
        var tagsParameter = entityType.FindProperty(nameof(Document.Tags))!.GetRelationalTypeMapping()
            .CreateParameter(command, "tags", tags);
        Assert.Same(tags, tagsParameter.Value);
        Assert.Equal(System.Data.DbType.Object, tagsParameter.DbType);

        // Converted keys are bound as their provider values.
        var settingsParameter = entityType.FindProperty(nameof(Document.Settings))!.GetRelationalTypeMapping()
            .CreateParameter(command, "settings", new Dictionary<Level, bool> { [Level.High] = true });
        Assert.Equal(new Dictionary<int, bool> { [(int)Level.High] = true }, Assert.IsType<Dictionary<int, bool>>(settingsParameter.Value));

        var nullParameter = entityType.FindProperty(nameof(Document.Tags))!.GetRelationalTypeMapping()
            .CreateParameter(command, "none", null);
        Assert.Equal(DBNull.Value, nullParameter.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dictionaries_compare_against_bound_map_parameters(bool duckLake)
    {
        await using var store = Store.Create(duckLake, DbPath);
        await using (var context = store.Create())
        {
            await context.Database.EnsureCreatedAsync();
            context.AddRange(
                new Document { Id = 1, Settings = new Dictionary<Level, bool> { [Level.High] = true } },
                new Document { Id = 2, Settings = new Dictionary<Level, bool> { [Level.Low] = false } });
            await context.SaveChangesAsync();
        }

        await using (var context = store.Create())
        {
            IReadOnlyDictionary<Level, bool> settings = new Dictionary<Level, bool> { [Level.High] = true };
            var ids = await context.Documents.Where(d => d.Settings == settings).Select(d => d.Id).ToListAsync();
            Assert.Equal([1], ids);
        }
    }

    [Fact]
    public void Map_comparer_hash_agrees_with_entry_equality_for_custom_key_comparers()
    {
        using var context = new Store(false, DbPath).Create();
        var comparer = context.Model.FindEntityType(typeof(Document))!
            .FindProperty(nameof(Document.Tags))!.GetValueComparer();
        var upper = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["A"] = "x", ["b"] = null };
        var lower = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["a"] = "x", ["B"] = null };

        Assert.True(comparer.Equals(upper, lower));
        Assert.Equal(comparer.GetHashCode(upper), comparer.GetHashCode(lower));
        Assert.False(comparer.Equals(upper, new Dictionary<string, string?> { ["A"] = "y", ["b"] = null }));
    }

    [Theory]
    [InlineData("MAP(VARCHAR, INTEGER)", typeof(Dictionary<string, int?>))]
    [InlineData("MAP(BIGINT, DECIMAL(18, 2))", typeof(Dictionary<long, decimal?>))]
    [InlineData("map(uuid, varchar)", typeof(Dictionary<Guid, string>))]
    [InlineData("INTEGER[][]", typeof(List<List<int>>))]
    [InlineData("VARCHAR[][][]", typeof(List<List<List<string>>>))]
    public void Store_types_resolve_to_dictionary_and_nested_list_mappings(string storeType, Type clrType)
    {
        using var context = new Store(false, DbPath).Create();

        var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(storeType);
        Assert.NotNull(mapping);
        Assert.Equal(clrType, mapping.ClrType);

        var inspection = context.Database.GetDuckDBStoreTypeMapping(storeType);
        Assert.Equal(DuckDBStoreTypeSupport.ScalarProperty, inspection.Support);
        Assert.Equal(clrType, inspection.ClrType);
    }

    [Theory]
    [InlineData("MAP(VARCHAR, BLOB)")]
    [InlineData("MAP(VARCHAR, INTEGER[])")]
    [InlineData("MAP(VARCHAR, MAP(VARCHAR, INTEGER))")]
    public void Maps_with_unsupported_entries_stay_raw_reader_only(string storeType)
    {
        using var context = new Store(false, DbPath).Create();

        Assert.Null(context.GetService<IRelationalTypeMappingSource>().FindMapping(storeType));
        Assert.Equal(DuckDBStoreTypeSupport.RawReaderOnly, context.Database.GetDuckDBStoreTypeMapping(storeType).Support);
    }

    [Fact]
    public void Unsupported_dictionary_shapes_are_rejected_during_model_building()
    {
        using var context = new UnsupportedContext(
            new DbContextOptionsBuilder<UnsupportedContext>().UseDuckDB($"DataSource={DbPath}").Options);

        var exception = Assert.Throws<InvalidOperationException>(() => context.Model);
        Assert.Contains(nameof(UnsupportedEntity.Blobs), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dictionaries_round_trip_through_BulkInsert_and_Upsert(bool duckLake)
    {
        await using var store = Store.Create(duckLake, DbPath);
        await using (var context = store.Create())
        {
            await context.Database.EnsureCreatedAsync();
            context.BulkInsert(
            [
                new Document
                {
                    Id = 1,
                    Tags = new() { ["colour"] = "blue", ["NULL"] = null },
                    Counts = new Dictionary<int, long> { [1] = 10, [-2] = long.MaxValue },
                    Scores = new Dictionary<string, double?> { ["pi"] = Math.PI, ["missing"] = null },
                    Settings = new Dictionary<Level, bool> { [Level.High] = true, [Level.Low] = false },
                    Dates = new Dictionary<Guid, DateTime> { [KnownGuid] = new DateTime(2024, 2, 29, 13, 14, 15, 123) },
                    Prices = new Dictionary<DateOnly, decimal> { [new DateOnly(2024, 1, 2)] = 12.34m }
                },
                new Document { Id = 2, Tags = [] }
            ]);
        }

        await using (var context = store.Create())
        {
            context.Upsert([new Document { Id = 2, Tags = new() { ["updated"] = "yes" } }]);
        }

        await using (var context = store.Create())
        {
            var documents = await context.Documents.OrderBy(d => d.Id).ToListAsync();
            Assert.Equal(new Dictionary<string, string?> { ["colour"] = "blue", ["NULL"] = null }, documents[0].Tags);
            Assert.Equal(new Dictionary<int, long> { [1] = 10, [-2] = long.MaxValue }, documents[0].Counts);
            Assert.Equal(Math.PI, documents[0].Scores!["pi"]);
            Assert.Null(documents[0].Scores!["missing"]);
            Assert.Equal(new Dictionary<Level, bool> { [Level.High] = true, [Level.Low] = false }, documents[0].Settings);
            Assert.Equal(new DateTime(2024, 2, 29, 13, 14, 15, 123), documents[0].Dates![KnownGuid]);
            Assert.Equal(12.34m, documents[0].Prices![new DateOnly(2024, 1, 2)]);
            Assert.Equal(new Dictionary<string, string?> { ["updated"] = "yes" }, documents[1].Tags);
            Assert.Null(documents[1].Counts);
        }
    }

    private static readonly Guid KnownGuid = new("8f1f6c9e-1c0e-4d63-9e0c-0a7b7f1d2a33");

    private sealed record ColumnType(string Name, string Type);

    public enum Level
    {
        Low = 1,
        High = 2
    }

    public sealed class Document
    {
        public int Id { get; set; }
        public Dictionary<string, string?>? Tags { get; set; }
        public IDictionary<int, long>? Counts { get; set; }
        public Dictionary<string, double?>? Scores { get; set; }
        public IReadOnlyDictionary<Level, bool>? Settings { get; set; }
        public Dictionary<Guid, DateTime>? Dates { get; set; }
        public Dictionary<DateOnly, decimal>? Prices { get; set; }
        public int[][]? Matrix { get; set; }
        public List<List<string?>>? Grid { get; set; }
    }

    public sealed class UnsupportedEntity
    {
        public int Id { get; set; }
        public Dictionary<string, byte[]>? Blobs { get; set; }
    }

    private sealed class DocumentContext(DbContextOptions<DocumentContext> options) : DbContext(options)
    {
        public DbSet<Document> Documents => Set<Document>();
    }

    private sealed class UnsupportedContext(DbContextOptions<UnsupportedContext> options) : DbContext(options)
    {
        public DbSet<UnsupportedEntity> Entities => Set<UnsupportedEntity>();
    }

    private sealed class Store(bool duckLake, string databasePath) : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"ducklake-map-{Guid.NewGuid():N}");

        public static Store Create(bool duckLake, string databasePath)
            => new(duckLake, databasePath);

        public DocumentContext Create()
        {
            var builder = new DbContextOptionsBuilder<DocumentContext>()
                .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            if (duckLake)
            {
                var dataPath = Path.Combine(_root, "data");
                Directory.CreateDirectory(dataPath);
                builder.UseDuckLake(Path.Combine(_root, "catalog.ducklake"), lake => lake.DataPath(dataPath));
            }
            else
            {
                builder.UseDuckDB($"DataSource={databasePath}");
            }

            return new DocumentContext(builder.Options);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}