using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Collections.ObjectModel;
using System.Collections;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>Describes one column returned by a dynamic DuckDB query.</summary>
/// <param name="Ordinal">The zero-based column ordinal.</param>
/// <param name="Name">The result-set column name.</param>
/// <param name="DuckDBTypeName">The DuckDB type name reported by DuckDB.NET.</param>
/// <param name="ClrType">The default CLR field type reported by DuckDB.NET.</param>
public sealed record DuckDBDynamicColumn(int Ordinal, string Name, string DuckDBTypeName, Type ClrType);

/// <summary>
///     Owns a streaming, unknown-shape DuckDB result set.
/// </summary>
/// <remarks>
///     Dispose the result after reading. Each yielded row owns its backing array and remains stable after the
///     reader advances. Values are the lossless CLR values returned by DuckDB.NET, with database nulls represented
///     by <see langword="null" />; the provider does not apply a JSON projection.
/// </remarks>
public sealed class DuckDBDynamicQueryResult : IDisposable, IAsyncDisposable
{
    private readonly RelationalDataReader _relationalReader;
    private readonly IConcurrencyDetector _concurrencyDetector;
    private int _enumerationStarted;
    private int _disposed;

    internal DuckDBDynamicQueryResult(
        RelationalDataReader relationalReader,
        IConcurrencyDetector concurrencyDetector)
    {
        _relationalReader = relationalReader;
        _concurrencyDetector = concurrencyDetector;

        var reader = relationalReader.DbDataReader;
        var schema = reader.GetColumnSchema();
        var columns = new DuckDBDynamicColumn[reader.FieldCount];
        for (var ordinal = 0; ordinal < columns.Length; ordinal++)
        {
            var schemaColumn = schema[ordinal];
            columns[ordinal] = new DuckDBDynamicColumn(
                ordinal,
                schemaColumn.ColumnName ?? reader.GetName(ordinal),
                schemaColumn.DataTypeName ?? reader.GetDataTypeName(ordinal),
                schemaColumn.DataType ?? reader.GetFieldType(ordinal));
        }

        Columns = new ReadOnlyCollection<DuckDBDynamicColumn>(columns);
    }

    /// <summary>Gets the columns in result-set ordinal order.</summary>
    public IReadOnlyList<DuckDBDynamicColumn> Columns { get; }

    /// <summary>Streams the rows in result-set order.</summary>
    /// <param name="cancellationToken">A token used to cancel asynchronous reads.</param>
    /// <returns>Rows whose values are aligned to <see cref="Columns" /> by ordinal.</returns>
    /// <exception cref="InvalidOperationException">The row stream has already been requested.</exception>
    /// <exception cref="ObjectDisposedException">The result has been disposed.</exception>
    public async IAsyncEnumerable<ReadOnlyMemory<object?>> ReadRowsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _enumerationStarted, 1) != 0)
        {
            throw new InvalidOperationException("A dynamic query result can be enumerated only once.");
        }

        var reader = _relationalReader.DbDataReader;
        while (true)
        {
            bool hasRow;
            object[]? values = null;
            using (_concurrencyDetector.EnterCriticalSection())
            {
                hasRow = await _relationalReader.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (hasRow)
                {
                    values = new object[reader.FieldCount];
                    reader.GetValues(values);
                    for (var ordinal = 0; ordinal < values.Length; ordinal++)
                    {
                        values[ordinal] = (await OwnValueAsync(values[ordinal], cancellationToken).ConfigureAwait(false))!;
                    }
                }
            }

            if (!hasRow)
            {
                yield break;
            }

            yield return values!;
        }
    }

    private static async ValueTask<object?> OwnValueAsync(object? value, CancellationToken cancellationToken)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        if (value is Stream stream)
        {
            var capacity = stream.CanSeek && stream.Length <= int.MaxValue
                ? checked((int)stream.Length)
                : 0;
            var ownedStream = capacity == 0 ? new MemoryStream() : new MemoryStream(capacity);
            try
            {
                await stream.CopyToAsync(ownedStream, cancellationToken).ConfigureAwait(false);
                ownedStream.Position = 0;
                return ownedStream;
            }
            catch
            {
                await ownedStream.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        if (value is IDictionary dictionary)
        {
            var keys = new object[dictionary.Count];
            dictionary.Keys.CopyTo(keys, 0);
            foreach (var key in keys)
            {
                var currentValue = dictionary[key];
                var ownedValue = await OwnValueAsync(currentValue, cancellationToken).ConfigureAwait(false);
                if (!ReferenceEquals(currentValue, ownedValue))
                {
                    dictionary[key] = ownedValue;
                }
            }

            return value;
        }

        if (value is IList list)
        {
            for (var index = 0; index < list.Count; index++)
            {
                var currentValue = list[index];
                var ownedValue = await OwnValueAsync(currentValue, cancellationToken).ConfigureAwait(false);
                if (!ReferenceEquals(currentValue, ownedValue))
                {
                    list[index] = ownedValue;
                }
            }
        }

        return value;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _relationalReader.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await _relationalReader.DisposeAsync().ConfigureAwait(false);
        }
    }
}