using BenchmarkDotNet.Attributes;
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;

namespace DuckDB.EFCoreProvider.Benchmarks;

/// <summary>
///     Measures the consumer-owned, tracked aggregate-reconciliation shape: stage updates for an aggregate and
///     its retained sub-aggregates, save once, then remove sub-aggregates absent from the incoming snapshot.
///     Setup, input construction, and result verification are outside the timed operation.
/// </summary>
[MemoryDiagnoser]
public class AggregateReconciliationBenchmarks
{
    private const int SubAggregateKinds = 4;
    private const int ExistingSubAggregatesPerKind = 5;
    private const int RetainedSubAggregatesPerKind = 3;

    private string _dbPath = "";
    private List<AggregateRoot> _incomingRoots = [];
    private List<AggregateSubAggregate> _incomingSubAggregates = [];
    private List<AggregateRoot> _seedRoots = [];
    private List<AggregateSubAggregate> _seedSubAggregates = [];
    private int _expectedSubAggregateCount;

    [Params(100, 500)]
    public int ParentCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _incomingRoots = new List<AggregateRoot>(ParentCount);
        _incomingSubAggregates = new List<AggregateSubAggregate>(
            ParentCount * SubAggregateKinds * RetainedSubAggregatesPerKind);
        _seedRoots = new List<AggregateRoot>(ParentCount);
        _seedSubAggregates = new List<AggregateSubAggregate>(
            ParentCount * SubAggregateKinds * ExistingSubAggregatesPerKind);

        var subAggregateId = 0;
        for (var parentIndex = 1; parentIndex <= ParentCount; parentIndex++)
        {
            var parentExternalId = AggregateBenchmarkIds.ForParent(parentIndex);
            _seedRoots.Add(new AggregateRoot
            {
                Id = parentIndex,
                ExternalId = parentExternalId,
                Payload = "before-" + parentIndex,
            });
            _incomingRoots.Add(new AggregateRoot
            {
                Id = parentIndex,
                ExternalId = parentExternalId,
                Payload = "after-" + parentIndex,
            });

            for (var kind = 0; kind < SubAggregateKinds; kind++)
            {
                for (var subAggregateIndex = 0;
                     subAggregateIndex < ExistingSubAggregatesPerKind;
                     subAggregateIndex++)
                {
                    var id = ++subAggregateId;
                    var externalId = AggregateBenchmarkIds.ForSubAggregate(id);
                    _seedSubAggregates.Add(new AggregateSubAggregate
                    {
                        Id = id,
                        ExternalId = externalId,
                        ParentExternalId = parentExternalId,
                        Kind = kind,
                        Payload = "before-" + id,
                    });

                    if (subAggregateIndex < RetainedSubAggregatesPerKind)
                    {
                        _incomingSubAggregates.Add(new AggregateSubAggregate
                        {
                            Id = id,
                            ExternalId = externalId,
                            ParentExternalId = parentExternalId,
                            Kind = kind,
                            Payload = "after-" + id,
                        });
                    }
                }
            }
        }
    }

    [IterationSetup(Target = nameof(StageUpsertOnlyAsync))]
    public void SetupUpsertOnly()
        => CreateIterationDatabase(reconcile: false);

    [IterationSetup(Target = nameof(StageUpsertAndReconcileAsync))]
    public void SetupReconciliation()
        => CreateIterationDatabase(reconcile: true);

    [IterationCleanup]
    public void IterationCleanup()
    {
        try
        {
            using var context = new AggregateReconciliationContext(_dbPath);
            var updatedRoots = context.Roots.Count(root => root.Payload.StartsWith("after-"));
            var updatedSubAggregates = context.SubAggregates.Count(row => row.Payload.StartsWith("after-"));
            var subAggregateCount = context.SubAggregates.Count();

            if (updatedRoots != ParentCount
                || updatedSubAggregates != _incomingSubAggregates.Count
                || subAggregateCount != _expectedSubAggregateCount)
            {
                throw new InvalidOperationException(
                    $"Aggregate reconciliation verification failed: roots={updatedRoots}/{ParentCount}, "
                    + $"updated sub-aggregates={updatedSubAggregates}/{_incomingSubAggregates.Count}, "
                    + $"sub-aggregates={subAggregateCount}/{_expectedSubAggregateCount}.");
            }
        }
        finally
        {
            BenchmarkFiles.DeleteDb(_dbPath);
        }
    }

    /// <summary>Stages and saves the incoming graph while leaving stale sub-aggregates in place.</summary>
    [Benchmark(Baseline = true)]
    public async Task<int> StageUpsertOnlyAsync()
        => await StageUpsertAndOptionallyReconcileAsync(reconcile: false);

    /// <summary>
    ///     Stages and saves the incoming graph, then applies the consumer's query, set difference, and 100-key
    ///     <see cref="RelationalQueryableExtensions.ExecuteDeleteAsync{TSource}(IQueryable{TSource}, CancellationToken)" />
    ///     reconciliation pattern for every sub-aggregate kind.
    /// </summary>
    [Benchmark]
    public async Task<int> StageUpsertAndReconcileAsync()
        => await StageUpsertAndOptionallyReconcileAsync(reconcile: true);

    private void CreateIterationDatabase(bool reconcile)
    {
        _dbPath = BenchmarkFiles.NewDbPath("aggregate_reconciliation");
        _expectedSubAggregateCount = reconcile ? _incomingSubAggregates.Count : _seedSubAggregates.Count;

        using var context = new AggregateReconciliationContext(_dbPath);
        context.Database.EnsureCreated();
        context.BulkInsert(_seedRoots);
        context.BulkInsert(_seedSubAggregates);
    }

    private async Task<int> StageUpsertAndOptionallyReconcileAsync(bool reconcile)
    {
        await using var context = new AggregateReconciliationContext(_dbPath);
        var processed = await StageRootsAsync(context);
        processed += await StageSubAggregatesAsync(context);
        processed += await context.SaveChangesAsync();

        if (reconcile)
        {
            processed += await DeleteOrphanedSubAggregatesAsync(context);
        }

        return processed;
    }

    private async Task<int> StageRootsAsync(AggregateReconciliationContext context)
    {
        var incomingIds = _incomingRoots.Select(root => root.ExternalId).ToList();
        var existingById = await context.Roots
            .Where(root => incomingIds.Contains(root.ExternalId))
            .ToDictionaryAsync(root => root.ExternalId);

        foreach (var incoming in _incomingRoots)
        {
            if (existingById.TryGetValue(incoming.ExternalId, out var existing))
            {
                incoming.Id = existing.Id;
                context.Entry(existing).CurrentValues.SetValues(incoming);
            }
            else
            {
                context.Roots.Add(incoming);
            }
        }

        return _incomingRoots.Count;
    }

    private async Task<int> StageSubAggregatesAsync(AggregateReconciliationContext context)
    {
        var processed = 0;
        foreach (var incomingForKind in _incomingSubAggregates.GroupBy(row => row.Kind))
        {
            var incomingRows = incomingForKind.ToList();
            var incomingIds = incomingRows.Select(row => row.ExternalId).ToList();
            var existingById = await context.SubAggregates
                .Where(row => row.Kind == incomingForKind.Key && incomingIds.Contains(row.ExternalId))
                .ToDictionaryAsync(row => row.ExternalId);

            foreach (var incoming in incomingRows)
            {
                if (existingById.TryGetValue(incoming.ExternalId, out var existing))
                {
                    incoming.Id = existing.Id;
                    context.Entry(existing).CurrentValues.SetValues(incoming);
                }
                else
                {
                    context.SubAggregates.Add(incoming);
                }
            }

            processed += incomingRows.Count;
        }

        return processed;
    }

    private async Task<int> DeleteOrphanedSubAggregatesAsync(AggregateReconciliationContext context)
    {
        var parentIds = _incomingRoots.Select(root => root.ExternalId).ToList();
        var deleted = 0;
        foreach (var incomingForKind in _incomingSubAggregates.GroupBy(row => row.Kind))
        {
            var incomingIds = incomingForKind.Select(row => row.ExternalId).ToHashSet();
            var existingIds = await context.SubAggregates
                .AsNoTracking()
                .Where(row => row.Kind == incomingForKind.Key && parentIds.Contains(row.ParentExternalId))
                .Select(row => row.ExternalId)
                .ToListAsync();

            foreach (var chunk in existingIds.Except(incomingIds).Chunk(100))
            {
                deleted += await context.SubAggregates
                    .Where(row => chunk.Contains(row.ExternalId))
                    .ExecuteDeleteAsync();
            }
        }

        return deleted;
    }
}

internal sealed class AggregateReconciliationContext(string dbPath) : DbContext
{
    internal DbSet<AggregateRoot> Roots => Set<AggregateRoot>();
    internal DbSet<AggregateSubAggregate> SubAggregates => Set<AggregateSubAggregate>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseDuckDB("DataSource=" + dbPath);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AggregateRoot>(entity =>
        {
            entity.ToTable("aggregate_roots");
            entity.Property(root => root.Id).ValueGeneratedNever();
            entity.HasAlternateKey(root => root.ExternalId);
        });

        modelBuilder.Entity<AggregateSubAggregate>(entity =>
        {
            entity.ToTable("aggregate_sub_aggregates");
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.HasAlternateKey(row => row.ExternalId);
            entity.HasIndex(row => new { row.ParentExternalId, row.Kind });
            entity.HasOne<AggregateRoot>()
                .WithMany()
                .HasForeignKey(row => row.ParentExternalId)
                .HasPrincipalKey(root => root.ExternalId);
        });
    }
}

internal sealed class AggregateRoot
{
    public int Id { get; set; }
    public Guid ExternalId { get; set; }
    public string Payload { get; set; } = "";
}

internal sealed class AggregateSubAggregate
{
    public int Id { get; set; }
    public Guid ExternalId { get; set; }
    public Guid ParentExternalId { get; set; }
    public int Kind { get; set; }
    public string Payload { get; set; } = "";
}

internal static class AggregateBenchmarkIds
{
    internal static Guid ForParent(int id)
        => new(id, 0x1000, 0x4000, 0x80, 0x00, 0x41, 0x47, 0x47, 0x52, 0x4f, 0x54);

    internal static Guid ForSubAggregate(int id)
        => new(id, 0x2000, 0x4000, 0x80, 0x00, 0x41, 0x47, 0x47, 0x53, 0x55, 0x42);
}