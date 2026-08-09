using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace DuckDB.EFCoreProvider.Extensions.Internal;

internal static class DuckDBParameterMetadataRegistry
{
    [ThreadStatic]
    private static CaptureScope? _current;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Register(DbParameter parameter, RelationalTypeMapping typeMapping)
        => _current?.Register(parameter, typeMapping);

    public static CaptureScope BeginCapture()
    {
        var scope = new CaptureScope(_current);
        _current = scope;
        return scope;
    }

    internal sealed class CaptureScope(CaptureScope? previous) : IDisposable
    {
        private readonly Dictionary<DbParameter, RelationalTypeMapping> _typeMappings =
            new(ReferenceEqualityComparer.Instance);
        private bool _disposed;

        public bool TryGetTypeMapping(DbParameter parameter, out RelationalTypeMapping? typeMapping)
            => _typeMappings.TryGetValue(parameter, out typeMapping);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (!ReferenceEquals(_current, this))
            {
                throw new InvalidOperationException("DuckDB parameter metadata capture scopes must be disposed in order.");
            }

            _current = previous;
            _disposed = true;
        }

        internal void Register(DbParameter parameter, RelationalTypeMapping typeMapping)
            => _typeMappings[parameter] = typeMapping;
    }
}