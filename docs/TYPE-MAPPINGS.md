# DuckDB type mappings

DuckDB.EFCoreProvider has two distinct type contracts:

1. **EF model mappings** describe the CLR types that can be mapped to entity properties and used in LINQ,
   migrations, and tracked writes.
2. **Raw reader mappings** describe the values returned by DuckDB.NET when SQL is executed through ADO.NET or
   the provider's dynamic-result API. Raw reader support does not imply that the same type can be persisted as an
   EF property.

## EF model mappings

| CLR type | Default DuckDB store type | Notes |
|---|---|---|
| `bool` | `BOOLEAN` | |
| `sbyte` | `TINYINT` | |
| `short` | `SMALLINT` | |
| `int` | `INTEGER` | |
| `long` | `BIGINT` | |
| `byte` | `UTINYINT` | |
| `ushort` | `USMALLINT` | |
| `uint` | `UINTEGER` | |
| `ulong` | `UBIGINT` | |
| `float` | `FLOAT` | |
| `double` | `DOUBLE` | |
| `decimal` | `DECIMAL(18,3)` | Configure precision and scale explicitly when the default is not suitable. |
| `string` | `VARCHAR` | `char` uses a fixed-length character mapping. |
| `byte[]` | `BLOB` | Materialized as bytes by the provider. |
| `Guid` | `UUID` | |
| `DateOnly` | `DATE` | |
| `DateTime` | `TIMESTAMP` | `TIMESTAMP_NS`, `TIMESTAMP_MS`, and `TIMESTAMP_S` can be selected explicitly. |
| `DateTimeOffset` | `TIMESTAMPTZ` | Non-zero-offset query behavior remains limited; see the capability map. |
| `TimeOnly` | `TIME` | `TIMETZ` and `TIME_NS` can be selected explicitly. |
| `TimeSpan` | `TIME` | Uses a provider value converter; this is not a dedicated `INTERVAL` mapping. |
| `JsonDocument`, `JsonElement`, `string` | `JSON` | Owned JSON through `ToJson()` is also supported. |
| `T[]`, `List<T>` | `T[]` | One-dimensional collections whose element type has a relational mapping. |
| EF complex property | `STRUCT(...)` | Opt in with `UseStructMapping()`; each nested field must have a supported scalar or nested complex mapping. |

Native scalar-property mappings for `MAP`, `UNION`, `HUGEINT`/`UHUGEINT`, and `VARIANT` are not currently
implemented. A raw reader may still return these types through DuckDB.NET.

Use `context.Database.GetDuckDBStoreTypeMapping(storeType)` when tooling needs the mapping contract at runtime.
The structured result distinguishes scalar properties, `STRUCT` complex properties, raw-reader-only types, and
unsupported types. Canonical integer store types and aliases preserve DuckDB signedness: for example, `INT8` and
`LONG` map to signed `long`, while `UBIGINT` and `UINT64` map to `ulong`.
Variable-length `T[]` lists have EF property mappings; fixed-size `T[n]` arrays are reported as raw-reader-only.
The [query command-plan guide](QUERY-COMMAND-PLANS.md#inspect-catalog-store-types) shows how an external compiler or
dynamic-model generator should consume this classification without maintaining its own store-type table.

## Raw DuckDB.NET reader mappings

The dynamic-result API deliberately returns the lossless values supplied by DuckDB.NET. Important mappings
include:

| DuckDB type | Raw CLR value |
|---|---|
| `HUGEINT`, `UHUGEINT` | `System.Numerics.BigInteger` |
| `DECIMAL` | `decimal` |
| `LIST`, fixed `ARRAY` | `List<T>` |
| `STRUCT` | `Dictionary<string, object>` |
| `MAP(K,V)` | `Dictionary<K,V>` |
| `BLOB` | `Stream` (the dynamic-result API clones it into row-owned memory before the reader advances) |

Nested collection values can contain further lists, structs, and maps. Use the reader's reported field type and
DuckDB type name rather than converting unknown values with `ToString()`.

DuckDB.NET is the source of truth for the complete raw mapping table. The provider preserves those values and
does not convert them into a JSON wire format.

## JSON and browser precision

JSON serialization is an application boundary, not an EF mapping operation. In particular:

- `Dictionary<string, object>` and `List<T>` should be passed to a serializer as their runtime types; calling
  `ToString()` first loses their structure.
- `BigInteger`, wide `BIGINT`/`UBIGINT`, and high-precision `DECIMAL` values may not be exactly representable by
  JavaScript's numeric type. The API that owns the JSON contract must choose whether to emit a number, a string,
  or a tagged representation.
- The provider does not silently round values to make them JSON-safe.

See the [DuckDB.NET type-mapping documentation](https://duckdb.net/docs/type-mapping.html) and
[composite-type documentation](https://duckdb.net/docs/composite-types.html) for the driver contract.
