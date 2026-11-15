using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace DuckDB.EFCoreProvider.ValueGeneration.Internal;

/// <summary>
///     Generates key values from blocks reserved by <see cref="DuckDBHiLoBlockAllocator" />.
/// </summary>
internal sealed class DuckDBHiLoValueGenerator<TValue> : HiLoValueGenerator<TValue>, IDuckDBClientKeyGenerator
{
    private readonly DuckDBHiLoBlockAllocator _allocator;
    private readonly DuckDBHiLoCounter _counter;
    private readonly int _blockSize;

    public DuckDBHiLoValueGenerator(
        HiLoValueGeneratorState state,
        DuckDBHiLoBlockAllocator allocator,
        DuckDBHiLoCounter counter,
        int blockSize)
        : base(state)
    {
        _allocator = allocator;
        _counter = counter;
        _blockSize = blockSize;
    }

    public override bool GeneratesTemporaryValues => false;

    protected override long GetNewLowValue()
        => _allocator.Allocate(_counter, _blockSize);

    protected override Task<long> GetNewLowValueAsync(CancellationToken cancellationToken = default)
        => _allocator.AllocateAsync(_counter, _blockSize, cancellationToken);

    // The Hi-Lo state ignores the entry, so raw writers such as BulkInsert can draw keys without tracking.
    object IDuckDBClientKeyGenerator.NextKey()
        => Next(null!)!;
}

/// <summary>
///     A generator that can assign keys to entities that are not tracked by the context.
/// </summary>
internal interface IDuckDBClientKeyGenerator
{
    object NextKey();
}