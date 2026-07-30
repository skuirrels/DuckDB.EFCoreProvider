using BenchmarkDotNet.Attributes;
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;

namespace DuckDB.EFCoreProvider.Benchmarks;

/// <summary>
///     Measures the referenced-principal update workaround against an unchanged unreferenced update.
///     The no-child case succeeds before and after the fix, allowing a valid same-workload comparison;
///     the with-child case is an after-only throughput measurement because v1.15.0 fails it.
/// </summary>
[MemoryDiagnoser]
public class ReferencedPrincipalUpdateBenchmarks
{
    private const int OperationsPerInvoke = 25;

    private BenchmarkContext? _context;
    private ReferencedParent? _referencedParent;
    private UnreferencedItem? _unreferencedItem;
    private string _dbPath = "";
    private bool _toggle;

    [IterationSetup(Target = nameof(ReferencedPrincipalWithoutChild))]
    public void SetupReferencedPrincipalWithoutChild()
        => Setup(includeChild: false, useReferencedParent: true);

    [IterationSetup(Target = nameof(ReferencedPrincipalWithChild))]
    public void SetupReferencedPrincipalWithChild()
        => Setup(includeChild: true, useReferencedParent: true);

    [IterationSetup(Target = nameof(UnreferencedUpdate))]
    public void SetupUnreferencedUpdate()
        => Setup(includeChild: false, useReferencedParent: false);

    [IterationCleanup]
    public void Cleanup()
    {
        _context?.Dispose();
        _context = null;
        _referencedParent = null;
        _unreferencedItem = null;
        DeleteDatabaseFiles(_dbPath);
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public int ReferencedPrincipalWithoutChild()
    {
        var affectedRows = 0;
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            _referencedParent!.Payload = NextPayload();
            affectedRows += _context!.SaveChanges();
        }

        return affectedRows;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public int ReferencedPrincipalWithChild()
    {
        var affectedRows = 0;
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            _referencedParent!.Payload = NextPayload();
            affectedRows += _context!.SaveChanges();
        }

        return affectedRows;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public int UnreferencedUpdate()
    {
        var affectedRows = 0;
        for (var i = 0; i < OperationsPerInvoke; i++)
        {
            _unreferencedItem!.Payload = NextPayload();
            affectedRows += _context!.SaveChanges();
        }

        return affectedRows;
    }

    private void Setup(bool includeChild, bool useReferencedParent)
    {
        _dbPath = Path.Combine(
            Path.GetTempPath(),
            $"duckdb-referenced-update-benchmark-{Guid.NewGuid():N}.db");

        using (var setup = new BenchmarkContext(_dbPath))
        {
            setup.Database.EnsureCreated();
            var externalId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var parent = new ReferencedParent
            {
                Id = 1,
                ExternalId = externalId,
                Payload = "before"
            };
            if (includeChild)
            {
                parent.Children.Add(
                    new ReferencedChild
                    {
                        Id = 1,
                        ParentExternalId = externalId
                    });
            }

            setup.Add(parent);
            setup.Add(new UnreferencedItem { Id = 1, Payload = "before" });
            setup.SaveChanges();
        }

        _context = new BenchmarkContext(_dbPath);
        if (useReferencedParent)
        {
            _referencedParent = _context.ReferencedParents.Single();
        }
        else
        {
            _unreferencedItem = _context.UnreferencedItems.Single();
        }
    }

    private string NextPayload()
    {
        _toggle = !_toggle;
        return _toggle ? "after-a" : "after-b";
    }

    private static void DeleteDatabaseFiles(string databasePath)
    {
        if (string.IsNullOrEmpty(databasePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(databasePath);
        var fileName = Path.GetFileName(databasePath);
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory, fileName + "*"))
        {
            File.Delete(path);
        }
    }

    private sealed class BenchmarkContext(string databasePath) : DbContext
    {
        public DbSet<ReferencedParent> ReferencedParents => Set<ReferencedParent>();
        public DbSet<UnreferencedItem> UnreferencedItems => Set<UnreferencedItem>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseDuckDB($"DataSource={databasePath}");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ReferencedParent>(entity =>
            {
                entity.Property(parent => parent.Id).ValueGeneratedNever();
                entity.HasAlternateKey(parent => parent.ExternalId);
                entity.Property(parent => parent.ComputedValue)
                    .HasComputedColumnSql("upper(\"Payload\")");
            });

            modelBuilder.Entity<ReferencedChild>(entity =>
            {
                entity.Property(child => child.Id).ValueGeneratedNever();
                entity.HasOne<ReferencedParent>()
                    .WithMany(parent => parent.Children)
                    .HasForeignKey(child => child.ParentExternalId)
                    .HasPrincipalKey(parent => parent.ExternalId);
            });

            modelBuilder.Entity<UnreferencedItem>(entity =>
            {
                entity.Property(item => item.Id).ValueGeneratedNever();
                entity.Property(item => item.ComputedValue)
                    .HasComputedColumnSql("upper(\"Payload\")");
            });
        }
    }

    private sealed class ReferencedParent
    {
        public long Id { get; set; }
        public Guid ExternalId { get; set; }
        public string Payload { get; set; } = "";
        public string? ComputedValue { get; private set; }
        public List<ReferencedChild> Children { get; set; } = [];
    }

    private sealed class ReferencedChild
    {
        public long Id { get; set; }
        public Guid ParentExternalId { get; set; }
    }

    private sealed class UnreferencedItem
    {
        public long Id { get; set; }
        public string Payload { get; set; } = "";
        public string? ComputedValue { get; private set; }
    }
}