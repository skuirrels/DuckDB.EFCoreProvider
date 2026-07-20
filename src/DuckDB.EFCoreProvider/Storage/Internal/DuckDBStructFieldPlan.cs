using Microsoft.EntityFrameworkCore.Storage;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     Describes one field of a DuckDB <c>STRUCT</c> column. Carries the
///     DuckDB field name (used both in the <c>STRUCT(...)</c> store type and
///     in struct literals <c>{'fieldName': value}</c>), the CLR
///     <see cref="PropertyInfo" /> used to read/write the value on the
///     complex object, and the field's relational type mapping used to render
///     the field's SQL literal. For nested struct fields, the field's
///     <see cref="FieldMapping" /> is itself a <see cref="DuckDBStructTypeMapping" />
///     with its own <c>Converter</c> for recursive pack/unpack.
/// </summary>
/// <remarks>
///     This is an internal API that supports the Entity Framework Core
///     infrastructure and may change without notice between releases.
/// </remarks>
public sealed record DuckDBStructFieldPlan(
    string FieldName,
    PropertyInfo Property,
    RelationalTypeMapping FieldMapping);