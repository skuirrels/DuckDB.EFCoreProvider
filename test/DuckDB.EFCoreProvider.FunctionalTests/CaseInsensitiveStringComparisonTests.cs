using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     End-to-end coverage for <c>UseCaseInsensitiveStringComparisons()</c> on both backend profiles.
/// </summary>
public sealed class CaseInsensitiveStringComparisonTests : DuckDBTestBase
{
    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Default_comparisons_remain_case_sensitive(bool duckLake)
        => Run(duckLake, caseInsensitive: false, context =>
        {
            var query = context.Rows.Where(row => row.Name == "po-abc-1");

            Assert.Empty(Ids(query));
            Assert.DoesNotContain("COLLATE", query.ToQueryString());
            Assert.Empty(Ids(context.Rows.Where(row => new[] { "po-abc-1", "po-xyz-2" }.Contains(row.Name))));
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Equality_and_inequality_ignore_case(bool duckLake)
        => Run(duckLake, caseInsensitive: true, context =>
        {
            var equal = context.Rows.Where(row => row.Name == "po-abc-1");
            var search = "ALPHA";

            Assert.Contains("COLLATE nocase", equal.ToQueryString());
            Assert.Equal([3], Ids(equal));
            Assert.Equal([1, 2], Ids(context.Rows.Where(row => row.Name == search)));
            Assert.Equal([1, 2], Ids(context.Rows.Where(row => search == row.Name)));
            Assert.Equal([1, 2], Ids(context.Rows.Where(row => string.Equals(row.Name, search))));

            // C# != keeps NULL names, so the inequality returns everything except both spellings of alpha.
            Assert.Equal([3, 4, 5], Ids(context.Rows.Where(row => row.Name != "aLpHa")));
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void In_and_list_contains_ignore_case(bool duckLake)
        => Run(duckLake, caseInsensitive: true, context =>
        {
            List<string> captured = ["po-abc-1", "PO-xyz-2"];

            Assert.Equal([3, 4], Ids(context.Rows.Where(row => new[] { "po-abc-1", "PO-xyz-2" }.Contains(row.Name))));
            Assert.Equal([3, 4], Ids(context.Rows.Where(row => captured.Contains(row.Name!))));
            Assert.Equal([1, 2, 5], Ids(context.Rows.Where(row => !captured.Contains(row.Name!))));
            Assert.Equal([1, 3], Ids(context.Rows.Where(row => row.Tags.Contains("RED"))));
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Null_parameters_and_correlated_comparisons_translate(bool duckLake)
        => Run(duckLake, caseInsensitive: true, context =>
        {
            string? missing = null;

            Assert.Equal([5], Ids(context.Rows.Where(row => row.Name == missing)));
            Assert.Equal([1, 2, 3, 4], Ids(context.Rows.Where(row => row.Name != missing)));
            Assert.Equal([5], Ids(context.Rows.Where(row => row.Name == null)));

            // A correlated comparison of two columns, as a join on a string key would produce.
            Assert.Equal(
                [1, 2, 3],
                Ids(context.Rows.Where(row => context.Codes.Any(code => code.Value == row.Name))));
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Searches_ordering_and_explicit_collations_are_unchanged(bool duckLake)
        => Run(duckLake, caseInsensitive: true, context =>
        {
            // StartsWith belongs to UseCaseInsensitiveStringSearches(), which is off here.
            Assert.Equal([2], Ids(context.Rows.Where(row => row.Name!.StartsWith("al"))));

            // EF.Functions.Collate on an operand keeps that collation.
            var collated = context.Rows.Where(row => EF.Functions.Collate(row.Name, "nfc") == "alpha");
            Assert.Equal([2], Ids(collated));
            Assert.DoesNotContain("nocase", collated.ToQueryString());
        });

    [ConditionalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Navigation_and_group_correlations_stay_case_sensitive(bool duckLake)
        => Run(duckLake, caseInsensitive: true, context =>
        {
            // Parents "A" and "a" differ only by case; the join to a navigation must not pair them.
            var navigation = context.Children
                .Where(child => child.Parent!.Code == "A")
                .OrderBy(child => child.Id)
                .Select(child => new { child.Id, child.Parent!.Code });
            Assert.Equal(
                [new { Id = 1, Code = "A" }, new { Id = 2, Code = "a" }, new { Id = 3, Code = "a" }],
                navigation.ToArray());
            Assert.Contains("ON c.\"ParentCode\" = p.\"Code\"", navigation.ToQueryString());

            var parents = context.Parents.Include(parent => parent.Children).OrderBy(parent => parent.Code).ToArray();
            Assert.Equal(["A", "a"], parents.Select(parent => parent.Code));
            Assert.Equal([1], parents[0].Children.Select(child => child.Id));
            Assert.Equal([2, 3], parents[1].Children.Select(child => child.Id).Order());
            context.ChangeTracker.Clear();

            // GROUP BY stays case-sensitive, so each group's elements must come from that group only.
            var groups = context.Children
                .GroupBy(child => child.Name)
                .Select(group => new { group.Key, Ids = group.Select(child => child.Id).ToList() })
                .ToArray()
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(["X", "x", "y"], groups.Select(group => group.Key));
            Assert.Equal([2], groups[0].Ids);
            Assert.Equal([1], groups[1].Ids);
            Assert.Equal([3], groups[2].Ids);

            var firstIds = context.Children
                .GroupBy(child => child.Name)
                .Select(group => group.OrderBy(child => child.Id).First().Id)
                .ToArray()
                .Order();
            Assert.Equal([1, 2, 3], firstIds);
        });

    [ConditionalFact]
    public void Compiled_models_keep_explicit_column_collations()
    {
        var reporter = new OperationReporter(new OperationReportHandler(_ => { }, _ => { }, _ => { }, _ => { }));
        using var context = new ComparisonContext(
            new DbContextOptionsBuilder<ComparisonContext>()
                .UseDuckDB("DataSource=:memory:", duckdb => duckdb.UseCaseInsensitiveStringComparisons())
                .Options,
            exactCollation: true);
        var serviceProvider = new DesignTimeServicesBuilder(
                typeof(CaseInsensitiveStringComparisonTests).Assembly,
                typeof(CaseInsensitiveStringComparisonTests).Assembly,
                reporter,
                [])
            .Build(context);

        var files = serviceProvider
            .GetRequiredService<ICompiledModelCodeGenerator>()
            .GenerateModel(
                context.GetService<IDesignTimeModel>().Model,
                new CompiledModelCodeGenerationOptions
                {
                    ModelNamespace = "Generated",
                    ContextType = typeof(ComparisonContext)
                });
        var code = string.Join(Environment.NewLine, files.Select(file => file.Code));

        Assert.Contains("\"DuckDB:RuntimeCollation\", \"nfc\"", code, StringComparison.Ordinal);
        Assert.Equal(
            "nfc",
            context.Model.FindEntityType(typeof(ComparisonRow))!
                .FindProperty(nameof(ComparisonRow.Exact))!
                .FindAnnotation("DuckDB:RuntimeCollation")?.Value);
    }

    [ConditionalFact]
    public void Columns_with_an_explicit_collation_keep_it()
    {
        var options = Options(duckLake: false, caseInsensitive: true, root: null);
        using var context = new ComparisonContext(options, exactCollation: true);
        Seed(context);

        var query = context.Rows.Where(row => row.Exact == "alpha");

        Assert.Equal([2], Ids(query));
        Assert.DoesNotContain("nocase", query.ToQueryString());
        Assert.Equal([1, 2], Ids(context.Rows.Where(row => row.Name == "alpha")));
    }

    [ConditionalFact]
    public void Opt_in_is_part_of_the_context_options_and_can_be_disabled()
    {
        var enabledOptions = FileOptions<ComparisonContext>(duckdb => duckdb.UseCaseInsensitiveStringComparisons());
        var disabledOptions = FileOptions<ComparisonContext>(duckdb => duckdb
            .UseCaseInsensitiveStringComparisons()
            .UseCaseInsensitiveStringComparisons(enable: false));

        Assert.True(enabledOptions.FindExtension<DuckDBOptionsExtension>()!.CaseInsensitiveStringComparisons);
        Assert.False(disabledOptions.FindExtension<DuckDBOptionsExtension>()!.CaseInsensitiveStringComparisons);
        Assert.False(enabledOptions.FindExtension<DuckDBOptionsExtension>()!.CaseInsensitiveStringSearches);
    }

    [ConditionalFact]
    public void Shared_internal_service_provider_rejects_mixed_case_insensitive_comparison_options()
    {
        using var serviceProvider = new ServiceCollection()
            .AddEntityFrameworkDuckDB()
            .BuildServiceProvider(validateScopes: true);
        var enabledOptions = new DbContextOptionsBuilder<ComparisonContext>()
            .UseDuckDB("DataSource=:memory:", duckdb => duckdb.UseCaseInsensitiveStringComparisons())
            .UseInternalServiceProvider(serviceProvider)
            .Options;
        var disabledOptions = new DbContextOptionsBuilder<ComparisonContext>()
            .UseDuckDB("DataSource=:memory:")
            .UseInternalServiceProvider(serviceProvider)
            .Options;

        using (var enabledContext = new ComparisonContext(enabledOptions))
        {
            _ = enabledContext.Model;
        }

        using var disabledContext = new ComparisonContext(disabledOptions);
        var exception = Assert.Throws<InvalidOperationException>(() => _ = disabledContext.Model);

        Assert.Contains(nameof(DuckDBOptionsExtension.CaseInsensitiveStringComparisons), exception.Message);
    }

    private static int[] Ids(IQueryable<ComparisonRow> query)
        => query.OrderBy(row => row.Id).Select(row => row.Id).ToArray();

    private DbContextOptions<ComparisonContext> Options(bool duckLake, bool caseInsensitive, string? root)
    {
        var builder = duckLake
            ? new DbContextOptionsBuilder<ComparisonContext>().UseDuckLake(
                Path.Combine(root!, "catalog.ducklake"),
                duckLake => duckLake.DataPath(Path.Combine(root!, "data")),
                duckdb => duckdb.UseCaseInsensitiveStringComparisons(caseInsensitive))
            : new DbContextOptionsBuilder<ComparisonContext>(
                FileOptions<ComparisonContext>(duckdb => duckdb.UseCaseInsensitiveStringComparisons(caseInsensitive)));

        return builder
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
    }

    private void Run(bool duckLake, bool caseInsensitive, Action<ComparisonContext> test)
    {
        var root = Path.Combine(Path.GetTempPath(), $"case_insensitive_comparison_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "data"));
        try
        {
            using var context = new ComparisonContext(Options(duckLake, caseInsensitive, root));
            Seed(context);

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

    private static void Seed(ComparisonContext context)
    {
        context.Database.EnsureCreated();
        context.Rows.AddRange(
            new ComparisonRow { Id = 1, Name = "Alpha", Exact = "Alpha", Tags = ["red", "green"] },
            new ComparisonRow { Id = 2, Name = "alpha", Exact = "alpha", Tags = ["blue"] },
            new ComparisonRow { Id = 3, Name = "PO-ABC-1", Exact = "PO-ABC-1", Tags = ["Red"] },
            new ComparisonRow { Id = 4, Name = "po-XYZ-2", Exact = "po-XYZ-2", Tags = [] },
            new ComparisonRow { Id = 5, Name = null, Exact = null, Tags = ["yellow"] });
        context.Codes.AddRange(
            new ComparisonCode { Id = 1, Value = "ALPHA" },
            new ComparisonCode { Id = 2, Value = "po-abc-1" });
        context.Parents.AddRange(new ComparisonParent { Code = "A" }, new ComparisonParent { Code = "a" });
        context.Children.AddRange(
            new ComparisonChild { Id = 1, ParentCode = "A", Name = "x" },
            new ComparisonChild { Id = 2, ParentCode = "a", Name = "X" },
            new ComparisonChild { Id = 3, ParentCode = "a", Name = "y" });
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    private sealed class ComparisonContext(DbContextOptions<ComparisonContext> options, bool exactCollation = false)
        : DbContext(options)
    {
        public DbSet<ComparisonRow> Rows => Set<ComparisonRow>();

        public DbSet<ComparisonCode> Codes => Set<ComparisonCode>();

        public DbSet<ComparisonParent> Parents => Set<ComparisonParent>();

        public DbSet<ComparisonChild> Children => Set<ComparisonChild>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ComparisonRow>(entity =>
            {
                entity.Property(row => row.Id).ValueGeneratedNever();
                if (exactCollation)
                {
                    // nfc normalises Unicode but compares case-sensitively.
                    entity.Property(row => row.Exact).UseCollation("nfc");
                }
            });
            modelBuilder.Entity<ComparisonCode>(entity => entity.Property(code => code.Id).ValueGeneratedNever());
            modelBuilder.Entity<ComparisonParent>().HasKey(parent => parent.Code);
            modelBuilder.Entity<ComparisonChild>(entity =>
            {
                entity.Property(child => child.Id).ValueGeneratedNever();
                entity.HasOne(child => child.Parent)
                    .WithMany(parent => parent.Children)
                    .HasForeignKey(child => child.ParentCode);
            });
        }
    }

    private sealed class ComparisonRow
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Exact { get; set; }

        public string[] Tags { get; set; } = [];
    }

    private sealed class ComparisonCode
    {
        public int Id { get; set; }

        public string Value { get; set; } = "";
    }

    private sealed class ComparisonParent
    {
        public string Code { get; set; } = "";

        public List<ComparisonChild> Children { get; set; } = [];
    }

    private sealed class ComparisonChild
    {
        public int Id { get; set; }

        public string ParentCode { get; set; } = "";

        public string Name { get; set; } = "";

        public ComparisonParent? Parent { get; set; }
    }
}
