using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class CaseInsensitiveStringSearchTests : DuckDBTestBase
{
    [ConditionalFact]
    public void Default_translation_remains_case_sensitive()
    {
        using var context = CreateContext(caseInsensitive: false);
        Seed(context);
        var search = "a";

        var query = context.SearchRows
            .Where(row => row.Name!.StartsWith(search))
            .OrderBy(row => row.Id);

        Assert.Equal(["alpha"], Names(query));
        Assert.Contains("starts_with", query.ToQueryString());
        Assert.DoesNotContain("ilike_escape", query.ToQueryString());
    }

    [ConditionalFact]
    public void Opt_in_applies_to_all_simple_string_searches_and_preserves_literal_wildcards()
    {
        using var context = CreateContext(caseInsensitive: true);
        Seed(context);

        Assert.Equal(
            ["Alpha", "alpha"],
            Names(context.SearchRows.Where(row => row.Name!.Contains("PH"))));
        Assert.Equal(
            ["Alpha", "alpha"],
            Names(context.SearchRows.Where(row => row.Name!.EndsWith("A"))));
        Assert.Equal(
            ["Alpha", "alpha", "A%c", "A_c"],
            Names(context.SearchRows.Where(row => row.Name!.StartsWith("a"))));
        Assert.Equal(
            ["Alpha", "alpha", "A%c", "A_c"],
            Names(context.SearchRows.Where(row => row.Name!.StartsWith('a'))));
        Assert.Equal(
            ["Alpha", "alpha"],
            Names(context.SearchRows.Where(row => row.Name!.EndsWith('A'))));
        Assert.Equal(
            ["A%c", "%lead"],
            Names(context.SearchRows.Where(row => row.Name!.Contains('%'))));
        Assert.Equal(
            ["A_c"],
            Names(context.SearchRows.Where(row => row.Name!.Contains("_"))));
        Assert.Equal(
            ["$cash"],
            Names(context.SearchRows.Where(row => row.Name!.Contains("$"))));
    }

    [ConditionalFact]
    public void Opt_in_keeps_captured_parameters_bound_and_null_safe()
    {
        using var context = CreateContext(caseInsensitive: true);
        Seed(context);
        var search = "ph";

        var query = context.SearchRows.Where(row => row.Name!.Contains(search));
        var plan = context.Database.GetDuckDBCommandPlan(query);

        Assert.Equal(["Alpha", "alpha"], Names(query));
        Assert.Contains("ilike_escape", plan.CommandText);
        Assert.Contains("replace", plan.CommandText);
        var parameter = Assert.Single(plan.Parameters);
        Assert.Equal(typeof(string), parameter.ClrType);
        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("VARCHAR", parameter.StoreType);
        Assert.Equal("ph", parameter.Value);
        Assert.DoesNotContain('$', parameter.Name);

        search = null!;
        Assert.Empty(query.ToArray());
    }

    [ConditionalFact]
    public void Opt_in_is_part_of_the_context_options_and_can_be_disabled()
    {
        var enabledOptions = FileOptions<SearchContext>(duckdb => duckdb.UseCaseInsensitiveStringSearches());
        var disabledOptions = FileOptions<SearchContext>(duckdb => duckdb
            .UseCaseInsensitiveStringSearches()
            .UseCaseInsensitiveStringSearches(enable: false));

        Assert.True(enabledOptions.FindExtension<DuckDBOptionsExtension>()!.CaseInsensitiveStringSearches);
        Assert.False(disabledOptions.FindExtension<DuckDBOptionsExtension>()!.CaseInsensitiveStringSearches);
    }

    [ConditionalFact]
    public void Shared_internal_service_provider_rejects_mixed_case_insensitive_search_options()
    {
        using var serviceProvider = new ServiceCollection()
            .AddEntityFrameworkDuckDB()
            .BuildServiceProvider(validateScopes: true);
        var enabledOptions = new DbContextOptionsBuilder<SearchContext>()
            .UseDuckDB(
                "DataSource=:memory:",
                duckdb => duckdb.UseCaseInsensitiveStringSearches())
            .UseInternalServiceProvider(serviceProvider)
            .Options;
        var disabledOptions = new DbContextOptionsBuilder<SearchContext>()
            .UseDuckDB("DataSource=:memory:")
            .UseInternalServiceProvider(serviceProvider)
            .Options;

        using (var enabledContext = new SearchContext(enabledOptions))
        {
            _ = enabledContext.Model;
        }

        using var disabledContext = new SearchContext(disabledOptions);
        var exception = Assert.Throws<InvalidOperationException>(() => _ = disabledContext.Model);

        Assert.Contains(nameof(DuckDBOptionsExtension.CaseInsensitiveStringSearches), exception.Message);
        Assert.Contains("UseInternalServiceProvider", exception.Message);
    }

    [ConditionalFact]
    public void DuckLake_profile_executes_case_insensitive_search()
    {
        var root = Path.Combine(Path.GetTempPath(), $"case_insensitive_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var metadataPath = Path.Combine(root, "catalog.ducklake");
        var dataPath = Path.Combine(root, "data");
        Directory.CreateDirectory(dataPath);

        try
        {
            var options = new DbContextOptionsBuilder<SearchContext>()
                .UseDuckLake(
                    metadataPath,
                    duckLake => duckLake.DataPath(dataPath),
                    duckdb => duckdb.UseCaseInsensitiveStringSearches())
                .EnableServiceProviderCaching(false)
                .ConfigureWarnings(warnings =>
                    warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                .Options;
            using var context = new SearchContext(options);
            Seed(context);

            var query = context.SearchRows.Where(row => row.Name!.Contains("PH"));

            Assert.Equal(["Alpha", "alpha"], Names(query));
            Assert.Contains("ilike_escape", query.ToQueryString());
            Assert.Contains("replace", query.ToQueryString());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private SearchContext CreateContext(bool caseInsensitive)
    {
        var options = new DbContextOptionsBuilder<SearchContext>(
                FileOptions<SearchContext>(duckdb => duckdb.UseCaseInsensitiveStringSearches(caseInsensitive)))
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings =>
                warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        return new SearchContext(options);
    }

    private static string[] Names(IQueryable<SearchRow> query)
        => query.OrderBy(row => row.Id).Select(row => row.Name!).ToArray();

    private static void Seed(SearchContext context)
    {
        context.Database.EnsureCreated();
        context.SearchRows.AddRange(
            new SearchRow { Id = 1, Name = "Alpha" },
            new SearchRow { Id = 2, Name = "alpha" },
            new SearchRow { Id = 3, Name = "A%c" },
            new SearchRow { Id = 4, Name = "A_c" },
            new SearchRow { Id = 5, Name = "$cash" },
            new SearchRow { Id = 6, Name = "%lead" },
            new SearchRow { Id = 7, Name = null });
        context.SaveChanges();
    }

    private sealed class SearchContext(DbContextOptions<SearchContext> options) : DbContext(options)
    {
        public DbSet<SearchRow> SearchRows => Set<SearchRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<SearchRow>(entity => entity.Property(row => row.Id).ValueGeneratedNever());
    }

    private sealed class SearchRow
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }
}