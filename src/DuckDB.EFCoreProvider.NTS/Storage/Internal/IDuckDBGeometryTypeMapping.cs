namespace DuckDB.EFCoreProvider.NTS.Storage.Internal;

/// <summary>
/// Marker interface for DuckDB geometry type mappings.
/// Used to detect when SQL parameters need wrapping with ST_GeomFromWKB()/ST_GeomFromText().
/// </summary>
public interface IDuckDBGeometryTypeMapping;

