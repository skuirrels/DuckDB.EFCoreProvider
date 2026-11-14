using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Relationships infer their requiredness from the nullability of their foreign key properties, as EF Core's
///     foreign key property discovery convention provides, while the provider keeps its non-cascading default delete
///     behaviour.
/// </summary>
public sealed class ForeignKeyRequirednessConventionTests : DuckDBTestBase
{
    [ConditionalFact]
    public void Relationships_infer_requiredness_from_foreign_key_nullability()
    {
        using var context = new BlogContext(FileOptions<BlogContext>());

        AssertRelationships(context);
    }

    [ConditionalFact]
    public void Relationships_infer_requiredness_from_foreign_key_nullability_on_DuckLake()
        => WithDuckLake(AssertRelationships);

    [ConditionalFact]
    public void Required_navigation_queries_use_inner_joins()
    {
        using var context = new BlogContext(FileOptions<BlogContext>());

        AssertQueries(context);
    }

    [ConditionalFact]
    public void Required_navigation_queries_use_inner_joins_on_DuckLake()
        => WithDuckLake(AssertQueries);

    [ConditionalFact]
    public void Inferred_requiredness_produces_no_migration_operations_against_optional_snapshots()
    {
        // Models built before requiredness was inferred had these relationships optional. The foreign key columns
        // and delete behaviour are unchanged, so upgrading must not generate a migration.
        using var current = new BlogContext(FileOptions<BlogContext>());
        using var legacy = new LegacyBlogContext(FileOptions<LegacyBlogContext>());

        Assert.False(legacy.Model.FindEntityType(typeof(Post))!.GetForeignKeys().Single(fk => fk.Properties[0].Name == nameof(Post.BlogId)).IsRequired);

        var differences = current.GetService<IMigrationsModelDiffer>().GetDifferences(
            RelationalModel(legacy),
            RelationalModel(current));

        Assert.Empty(differences);

        static IRelationalModel RelationalModel(DbContext context)
            => context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
    }

    private static void AssertRelationships(BlogContext context)
    {
        var post = context.Model.FindEntityType(typeof(Post))!;

        var relationships = post.GetForeignKeys()
            .OrderBy(foreignKey => foreignKey.Properties[0].Name)
            .Select(foreignKey => (foreignKey.Properties[0].Name, foreignKey.IsRequired, foreignKey.DeleteBehavior))
            .ToList();

        Assert.Equal(
            [
                // Shadow foreign key: EF creates a nullable property, so the relationship is optional.
                ("AuthorId", false, DeleteBehavior.ClientSetNull),
                // Non-nullable property discovered by name.
                (nameof(Post.BlogId), true, DeleteBehavior.ClientSetNull),
                // Non-nullable property configured with HasForeignKey.
                (nameof(Post.CategoryRef), true, DeleteBehavior.ClientSetNull),
                // Nullable property discovered by name.
                (nameof(Post.EditorId), false, DeleteBehavior.ClientSetNull),
                // Explicit configuration wins over inference.
                (nameof(Post.ReviewerId), false, DeleteBehavior.ClientSetNull),
                // Explicitly required shadow foreign key.
                ("SponsorId", true, DeleteBehavior.ClientSetNull),
            ],
            relationships);

        // Inference changes requiredness only. The explicitly required shadow foreign key keeps the nullable
        // property and column it had before, even though other relationships on the entity move onto new
        // properties, so existing models do not gain an AlterColumn migration.
        var sponsor = post.FindProperty("SponsorId")!;
        Assert.True(post.GetForeignKeys().Single(fk => fk.Properties[0] == sponsor).IsRequired);
        Assert.Equal(typeof(int?), sponsor.ClrType);
        Assert.True(sponsor.IsNullable);
    }

    private static void AssertQueries(BlogContext context)
    {
        context.Database.EnsureCreated();
        var blog = new Blog { Id = 1, Name = "Engineering" };
        var category = new Category { Id = 1, Name = "Databases" };
        var person = new Person { Id = 1, Name = "Ada" };
        context.AddRange(
            new Post { Id = 1, Title = "Joins", Blog = blog, Category = category, Reviewer = person, Sponsor = person, Editor = person },
            new Post { Id = 2, Title = "Keys", Blog = blog, Category = category, Reviewer = person, Sponsor = person });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var query = context.Set<Post>()
            .OrderBy(post => post.Id)
            .Select(post => new { post.Title, Blog = post.Blog!.Name, Category = post.Category!.Name, Editor = post.Editor!.Name });

        var sql = query.ToQueryString();
        Assert.Contains("INNER JOIN \"Blog\" AS b ON p.\"BlogId\" = b.\"Id\"", sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN \"Category\" AS c ON p.\"CategoryRef\" = c.\"Id\"", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN \"Person\" AS p0 ON p.\"EditorId\" = p0.\"Id\"", sql, StringComparison.Ordinal);

        Assert.Equal(
            [("Joins", "Engineering", "Databases", "Ada"), ("Keys", "Engineering", "Databases", null)],
            query.AsEnumerable().Select(row => (row.Title, row.Blog, row.Category, (string?)row.Editor)).ToList());
    }

    private static void WithDuckLake(Action<BlogContext> assert)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ducklake_fk_required_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "data"));

        try
        {
            var options = new DbContextOptionsBuilder<BlogContext>()
                .UseDuckLake(Path.Combine(root, "metadata.ducklake"), duckLake => duckLake.DataPath(Path.Combine(root, "data")))
                .Options;

            using var context = new BlogContext(options);
            assert(context);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private class BlogContext(DbContextOptions options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Blog>().Property(blog => blog.Id).ValueGeneratedNever();
            modelBuilder.Entity<Category>().Property(category => category.Id).ValueGeneratedNever();
            modelBuilder.Entity<Person>().Property(person => person.Id).ValueGeneratedNever();
            modelBuilder.Entity<Post>().Property(post => post.Id).ValueGeneratedNever();
            modelBuilder.Entity<Post>().HasOne(post => post.Category).WithMany().HasForeignKey(post => post.CategoryRef);
            modelBuilder.Entity<Post>().HasOne(post => post.Reviewer).WithMany().HasForeignKey(post => post.ReviewerId).IsRequired(false);
            modelBuilder.Entity<Post>().HasOne(post => post.Editor).WithMany().HasForeignKey(post => post.EditorId);
            modelBuilder.Entity<Post>().HasOne(post => post.Author).WithMany();
            modelBuilder.Entity<Post>().HasOne(post => post.Sponsor).WithMany().IsRequired();
        }
    }

    // The relationships as earlier provider versions modelled them.
    private sealed class LegacyBlogContext(DbContextOptions<LegacyBlogContext> options) : BlogContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Post>().HasOne(post => post.Blog).WithMany(blog => blog.Posts).IsRequired(false);
            modelBuilder.Entity<Post>().HasOne(post => post.Category).WithMany().IsRequired(false);
        }
    }

    private sealed class Blog
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public List<Post> Posts { get; } = [];
    }

    private sealed class Category
    {
        public int Id { get; set; }
        public required string Name { get; set; }
    }

    private sealed class Person
    {
        public int Id { get; set; }
        public required string Name { get; set; }
    }

    private sealed class Post
    {
        public int Id { get; set; }
        public required string Title { get; set; }
        public int BlogId { get; set; }
        public int CategoryRef { get; set; }
        public int ReviewerId { get; set; }
        public int? EditorId { get; set; }
        public Blog? Blog { get; set; }
        public Category? Category { get; set; }
        public Person? Reviewer { get; set; }
        public Person? Editor { get; set; }
        public Person? Author { get; set; }
        public Person? Sponsor { get; set; }
    }
}