using DuckDB.EFCoreProvider.Internal;

namespace Microsoft.EntityFrameworkCore.TestUtilities;

/// <summary>
///     Identifies the loaded native engine generation for tests that assert DuckDB's own error text. Provider
///     behaviour is asserted through <c>IDuckDBEngineCapabilities</c>; engine messages follow the engine release.
/// </summary>
public static class DuckDBTestEngine
{
    /// <summary><see langword="true" /> when the loaded native library is DuckDB 2.0 or later.</summary>
    public static bool IsDuckDB20OrLater => DuckDBNativeEngine.Version >= DuckDBNativeEngine.Cyanoptera;
}
