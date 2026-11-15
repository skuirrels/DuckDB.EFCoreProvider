using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace DuckDB.EFCoreProvider.ValueGeneration.Internal;

/// <summary>
///     Selects <see cref="DuckDBHiLoValueGenerator{TValue}" /> for Hi-Lo key properties.
/// </summary>
internal sealed class DuckDBValueGeneratorSelector : RelationalValueGeneratorSelector
{
    private readonly DuckDBHiLoStateCache _stateCache;
    private readonly DuckDBHiLoBlockAllocator _allocator;
    private readonly IDbContextOptions _options;
    private readonly IRelationalConnection _connection;
    private readonly Dictionary<IProperty, ValueGenerator> _hiLoGenerators = [];
    private readonly Dictionary<(string? Schema, string Table, int BlockSize), HiLoValueGeneratorState> _privateStates = [];

    public DuckDBValueGeneratorSelector(
        ValueGeneratorSelectorDependencies dependencies,
        DuckDBHiLoStateCache stateCache,
        DuckDBHiLoBlockAllocator allocator,
        IDbContextOptions options,
        IRelationalConnection connection)
        : base(dependencies)
    {
        _stateCache = stateCache;
        _allocator = allocator;
        _options = options;
        _connection = connection;
    }

    /// <inheritdoc />
    public override bool TrySelect(IProperty property, ITypeBase typeBase, out ValueGenerator? valueGenerator)
    {
        if (property.GetValueGenerationStrategy() != DuckDBValueGenerationStrategy.HiLo)
        {
            return base.TrySelect(property, typeBase, out valueGenerator);
        }

        lock (_hiLoGenerators)
        {
            if (!_hiLoGenerators.TryGetValue(property, out var generator))
            {
                generator = CreateHiLoGenerator(property);
                _hiLoGenerators.Add(property, generator);
            }

            valueGenerator = generator;
            return true;
        }
    }

    private ValueGenerator CreateHiLoGenerator(IProperty property)
    {
        var counter = DuckDBHiLoCounter.For(property);
        var blockSize = property.GetHiLoBlockSize();
        var identity = DuckDBHiLoStateCache.GetDatabaseIdentity(_options, _connection.ConnectionString);
        HiLoValueGeneratorState state;
        if (identity is not null)
        {
            state = _stateCache.GetOrAdd(identity, counter.Schema, counter.TableName, blockSize);
        }
        else if (!_privateStates.TryGetValue((counter.Schema, counter.TableName, blockSize), out state!))
        {
            state = new HiLoValueGeneratorState(blockSize);
            _privateStates.Add((counter.Schema, counter.TableName, blockSize), state);
        }

        var clrType = property.ClrType.UnwrapNullableType();
        return clrType switch
        {
            _ when clrType == typeof(long) => new DuckDBHiLoValueGenerator<long>(state, _allocator, counter, blockSize),
            _ when clrType == typeof(int) => new DuckDBHiLoValueGenerator<int>(state, _allocator, counter, blockSize),
            _ when clrType == typeof(short) => new DuckDBHiLoValueGenerator<short>(state, _allocator, counter, blockSize),
            _ when clrType == typeof(sbyte) => new DuckDBHiLoValueGenerator<sbyte>(state, _allocator, counter, blockSize),
            _ when clrType == typeof(byte) => new DuckDBHiLoValueGenerator<byte>(state, _allocator, counter, blockSize),
            _ when clrType == typeof(ushort) => new DuckDBHiLoValueGenerator<ushort>(state, _allocator, counter, blockSize),
            _ when clrType == typeof(uint) => new DuckDBHiLoValueGenerator<uint>(state, _allocator, counter, blockSize),
            _ when clrType == typeof(ulong) => new DuckDBHiLoValueGenerator<ulong>(state, _allocator, counter, blockSize),
            _ => throw new InvalidOperationException(
                $"Hi-Lo key generation does not support the CLR type '{clrType.ShortDisplayName()}' of "
                + $"'{property.DeclaringType.DisplayName()}.{property.Name}'.")
        };
    }
}