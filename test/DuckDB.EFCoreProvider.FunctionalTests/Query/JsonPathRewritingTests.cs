using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Query.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Linq.Expressions;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class JsonPathRewritingTests : DuckDBTestBase
{
    [ConditionalFact]
    public void Json_each_rewrite_preserves_path_segments_before_a_rewritten_array_index()
    {
        var json = new SqlConstantExpression("{}", typeof(string), typeMapping: null);
        var originalIndex = new SqlConstantExpression(0, typeof(int), typeMapping: null);
        var rewrittenIndex = new SqlConstantExpression(1, typeof(int), typeMapping: null);
        var expression = new DuckDBJsonEachExpression(
            "items",
            json,
            [new PathSegment("Items"), new PathSegment(originalIndex), new PathSegment("Children")]);

        var rewritten = Assert.IsType<DuckDBJsonEachExpression>(
            new ReplacingExpressionVisitor(originalIndex, rewrittenIndex).Visit(expression));

        Assert.NotSame(expression, rewritten);
        Assert.Equal("Items", rewritten.Path![0].PropertyName);
        Assert.Same(rewrittenIndex, rewritten.Path[1].ArrayIndex);
        Assert.Equal("Children", rewritten.Path[2].PropertyName);
    }

    [ConditionalFact]
    public void Nullability_processing_recognizes_only_typed_json_each_expressions()
    {
        using var context = new JsonPathContext(FileOptions<JsonPathContext>());
        var processor = CreateNullabilityProcessor(context);
        var parameter = new SqlParameterExpression(
            "items",
            "items",
            typeof(string),
            nullable: false,
            ParameterTranslationMode.Parameter,
            typeMapping: null);
        var typedJsonEach = new DuckDBJsonEachExpression(
            "typed_items",
            parameter,
            [new PathSegment("Items")]);
        var sameNameGenericFunction = new TableValuedFunctionExpression(
            "generic_items",
            "json_each",
            [parameter]);

        Assert.True(processor.TryGetCollection(typedJsonEach, out var collection));
        Assert.Same(parameter, collection);
        Assert.False(processor.TryGetCollection(sameNameGenericFunction, out _));

        var replacement = new SqlParameterExpression(
            "replacement",
            "replacement",
            typeof(string),
            nullable: false,
            ParameterTranslationMode.Parameter,
            typeMapping: null);
        var updated = Assert.IsType<DuckDBJsonEachExpression>(
            processor.ReplaceCollectionParameter(typedJsonEach, replacement));

        Assert.Same(replacement, updated.JsonExpression);
        Assert.Same(typedJsonEach.Path, updated.Path);
    }

    [ConditionalFact]
    public void Nullability_processing_recognizes_only_typed_unnest_expressions()
    {
        using var context = new JsonPathContext(FileOptions<JsonPathContext>());
        var processor = CreateNullabilityProcessor(context);
        var parameter = new SqlParameterExpression(
            "items",
            "items",
            typeof(int[]),
            nullable: false,
            ParameterTranslationMode.Parameter,
            typeMapping: null);
        var typedUnnest = new DuckDBUnnestExpression("typed_items", parameter, "value");
        var sameNameGenericFunction = new TableValuedFunctionExpression(
            "generic_items",
            "unnest",
            [parameter]);

        Assert.True(processor.TryGetCollection(typedUnnest, out var collection));
        Assert.Same(parameter, collection);
        Assert.False(processor.TryGetCollection(sameNameGenericFunction, out _));

        var replacement = new SqlParameterExpression(
            "replacement",
            "replacement",
            typeof(int[]),
            nullable: false,
            ParameterTranslationMode.Parameter,
            typeMapping: null);
        var updated = Assert.IsType<DuckDBUnnestExpression>(
            processor.ReplaceCollectionParameter(typedUnnest, replacement));

        Assert.Same(replacement, updated.Array);
        Assert.Equal(typedUnnest.ColumnName, updated.ColumnName);
        Assert.Equal(typedUnnest.WithOrdinality, updated.WithOrdinality);
    }

    [ConditionalFact]
    public void Parameterized_nested_json_collection_index_executes()
    {
        using var context = CreateSeededContext();

        var itemIndex = 0;
        var ownerId = context.Owners
            .Where(owner => owner.Details.Items[itemIndex].Children.Any(child => child.Name == "target"))
            .Select(owner => owner.Id)
            .Single();

        Assert.Equal(1, ownerId);
    }

    [ConditionalFact]
    public void Nested_json_collection_count_executes()
    {
        using var context = CreateSeededContext();

        var childCount = context.Owners
            .Select(owner => owner.Details.Items.Count)
            .Single();

        Assert.Equal(1, childCount);
    }

    private JsonPathContext CreateSeededContext()
    {
        var context = new JsonPathContext(FileOptions<JsonPathContext>());
        context.Database.EnsureCreated();
        context.Add(
            new JsonOwner
            {
                Id = 1,
                Details = new JsonDetails
                {
                    Items =
                    [
                        new JsonItem
                        {
                            Children = [new JsonChild { Name = "target" }]
                        }
                    ]
                }
            });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        return context;
    }

    private static TestSqlNullabilityProcessor CreateNullabilityProcessor(DbContext context)
        => new(
            context.GetService<RelationalParameterBasedSqlProcessorDependencies>(),
            new RelationalParameterBasedSqlProcessorParameters(
                useRelationalNulls: false,
                ParameterTranslationMode.Parameter));

    private sealed class ReplacingExpressionVisitor(Expression source, Expression replacement) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node)
            => ReferenceEquals(node, source) ? replacement : base.Visit(node);
    }

    private sealed class TestSqlNullabilityProcessor(
        RelationalParameterBasedSqlProcessorDependencies dependencies,
        RelationalParameterBasedSqlProcessorParameters parameters)
        : DuckDBSqlNullabilityProcessor(dependencies, parameters)
    {
        public bool TryGetCollection(TableExpressionBase table, out Expression? collection)
            => IsCollectionTable(table, out collection);

        public TableExpressionBase ReplaceCollectionParameter(
            TableExpressionBase table,
            SqlParameterExpression parameter)
            => UpdateParameterCollection(table, parameter);
    }

    private sealed class JsonPathContext(DbContextOptions<JsonPathContext> options) : DbContext(options)
    {
        public DbSet<JsonOwner> Owners => Set<JsonOwner>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<JsonOwner>(owner =>
            {
                owner.HasKey(entity => entity.Id);
                owner.OwnsOne(
                    entity => entity.Details,
                    details =>
                    {
                        details.ToJson();
                        details.OwnsMany(
                            value => value.Items,
                            items => items.OwnsMany(value => value.Children));
                    });
            });
        }
    }

    private sealed class JsonOwner
    {
        public int Id { get; set; }

        public JsonDetails Details { get; set; } = new();
    }

    private sealed class JsonDetails
    {
        public List<JsonItem> Items { get; set; } = [];
    }

    private sealed class JsonItem
    {
        public List<JsonChild> Children { get; set; } = [];
    }

    private sealed class JsonChild
    {
        public string Name { get; set; } = null!;
    }
}