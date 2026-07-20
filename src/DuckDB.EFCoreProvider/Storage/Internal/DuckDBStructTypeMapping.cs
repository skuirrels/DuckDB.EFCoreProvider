using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.EFCoreProvider.Storage.ValueConverters;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Data.Common;
using System.Text;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     A DuckDB <c>STRUCT</c> type mapping. An instance carries an ordered list
///     of <c>(fieldName, fieldTypeMapping)</c> pairs so it can both:
///     <list type="bullet">
///         <item>Render the DDL store type as <c>STRUCT(f1 TYPE1, f2 TYPE2, ...)</c>.</item>
///         <item>Render a provider value (<c>IReadOnlyDictionary&lt;string, object?&gt;</c>) as
///             <c>{'f1': v1, 'f2': v2}::STRUCT(...)</c>.</item>
///     </list>
///     Nested struct fields are represented by a <see cref="DuckDBStructTypeMapping" />
///     as the field's <see cref="RelationalTypeMapping" />, allowing arbitrary
///     nesting depth. The field order matches the relational model's enumeration
///     so the resulting SQL is deterministic.
/// </summary>
/// <remarks>
///     This is an internal API that supports the Entity Framework Core
///     infrastructure. It may change or be removed without notice between releases.
/// </remarks>
public sealed class DuckDBStructTypeMapping : RelationalTypeMapping
{
    /// <summary>
    ///     Ordered list of struct field descriptors, in declaration order. The
    ///     order is encoded into the store type and literal so SQL generation is
    ///     deterministic.
    /// </summary>
    public IReadOnlyList<DuckDBStructFieldPlan> Fields { get; }

    /// <summary>
    ///     Initializes a new <see cref="DuckDBStructTypeMapping" /> for a
    ///     complex CLR type backed by a single DuckDB <c>STRUCT</c> column.
    /// </summary>
    /// <param name="clrType">The model CLR type mapped to a STRUCT column (e.g. <c>Location</c>).</param>
    /// <param name="fields">Ordered list of struct field plans describing each field of the STRUCT column.</param>
    /// <param name="converter">
    ///     A <see cref="ValueConverter" /> that packs/unpacks <paramref name="clrType" />
    ///     to/from <c>IReadOnlyDictionary&lt;string, object?&gt;</c>. Usually a
    ///     <see cref="DuckDBStructValueConverter{TClr}" />.
    /// </param>
    /// <param name="comparer">Optional <see cref="ValueComparer" /> for change tracking.</param>
    public DuckDBStructTypeMapping(
        Type clrType,
        IReadOnlyList<DuckDBStructFieldPlan> fields,
        ValueConverter? converter,
        ValueComparer? comparer)
        : base(CreateParameters(clrType, BuildStoreType(fields), converter, comparer))
    {
        Fields = fields;
    }

    private DuckDBStructTypeMapping(
        RelationalTypeMappingParameters parameters,
        IReadOnlyList<DuckDBStructFieldPlan> fields)
        : base(parameters)
    {
        Fields = fields;
    }

    private static RelationalTypeMappingParameters CreateParameters(
        Type clrType,
        string storeType,
        ValueConverter? converter,
        ValueComparer? comparer)
        => new(new CoreTypeMappingParameters(clrType, converter, comparer), storeType);

    /// <summary>
    ///     Renders the <c>STRUCT(f1 TYPE1, f2 TYPE2, ...)</c> store type string for
    ///     the supplied ordered field list.
    /// </summary>
    /// <param name="fields">Ordered list of struct field descriptors.</param>
    /// <returns>A valid DuckDB <c>STRUCT(...)</c> store type string.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="fields" /> is empty — DuckDB does not permit
    ///     zero-field structs.
    /// </exception>
    internal static string BuildStoreType(IReadOnlyList<DuckDBStructFieldPlan> fields)
    {
        if (fields.Count == 0)
        {
            throw new ArgumentException(
                "A DuckDB STRUCT column must have at least one field.", nameof(fields));
        }

        var sb = new StringBuilder("STRUCT(");
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            var (fieldName, _, fieldMapping) = fields[i];
            sb.Append(fieldName).Append(' ').Append(fieldMapping.StoreType);
        }

        sb.Append(')');
        return sb.ToString();
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new DuckDBStructTypeMapping(parameters, Fields);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        if (value is not IReadOnlyDictionary<string, object?> dict)
        {
            throw new ArgumentException(
                $"DuckDBStructTypeMapping expected a IReadOnlyDictionary<string,object?> for SQL literal "
                + $"generation, got '{value?.GetType().FullName ?? "null"}'.",
                nameof(value));
        }

        var sb = new StringBuilder("{");
        for (var i = 0; i < Fields.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            var (fieldName, _, fieldMapping) = Fields[i];
            if (!dict.TryGetValue(fieldName, out var fieldValue))
            {
                var expected = string.Join(", ", Fields.Select(f => $"'{f.FieldName}'"));
                throw new InvalidOperationException(
                    $"Struct literal is missing field '{fieldName}'. Expected fields: {expected}.");
            }

            sb.Append('\'').Append(fieldName).Append("': ")
              .Append(fieldMapping.GenerateProviderValueSqlLiteral(fieldValue));
        }

        sb.Append("}::").Append(StoreType);
        return sb.ToString();
    }

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        // DuckDBParameter binding is inferred from the dictionary value — there is no
        // public DuckDBDbType setter for STRUCT. Strip the leading `$` from the
        // parameter name (DuckDB uses `$1` for positional syntax).
#pragma warning disable EF1001
        ((DuckDBParameter)parameter).RemoveDollarSign();
#pragma warning restore EF1001
        base.ConfigureParameter(parameter);
    }
}