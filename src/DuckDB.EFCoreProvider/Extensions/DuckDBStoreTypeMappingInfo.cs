namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>Describes how a DuckDB store type can participate in EF Core models.</summary>
public enum DuckDBStoreTypeSupport
{
    /// <summary>The type can be mapped to a scalar entity property.</summary>
    ScalarProperty,

    /// <summary>The type can be mapped through an EF complex property rather than a scalar property.</summary>
    ComplexProperty,

    /// <summary>The type can be materialized by DuckDB.NET raw readers but has no EF property mapping.</summary>
    RawReaderOnly,

    /// <summary>The provider does not recognize the type as a supported model or raw-reader contract.</summary>
    Unsupported
}

/// <summary>Reports the provider mapping contract for one DuckDB store type.</summary>
public sealed record DuckDBStoreTypeMappingInfo(
    string StoreType,
    DuckDBStoreTypeSupport Support,
    Type? ClrType,
    string? TypeMapping,
    DuckDBStoreTypeMappingInfo? ElementType = null);