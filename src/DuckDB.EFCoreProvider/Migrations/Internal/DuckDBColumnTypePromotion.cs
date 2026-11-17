using System.Globalization;

namespace DuckDB.EFCoreProvider.Migrations.Internal;

/// <summary>How a column's store type changes between two migration states.</summary>
internal enum DuckDBColumnTypeChange
{
    /// <summary>Both store types name the same DuckDB type.</summary>
    Unchanged,

    /// <summary>The new type is a widening promotion of the old one, which every engine profile applies in place.</summary>
    Widening,

    /// <summary>The change converts values to another type and may narrow, reinterpret or fail on existing data.</summary>
    Conversion
}

/// <summary>
///     Classifies store type changes for engine profiles that can only promote column types in place, such as DuckLake,
///     which rejects other changes with "only widening type promotions are allowed".
/// </summary>
/// <remarks>
///     The promotions are a conservative subset of those DuckLake 1.5.6 accepts: integers to wider integers (an
///     unsigned type only to a strictly wider signed one), integers and decimals to floating point, integers to decimals
///     with enough integer digits, decimals that keep their scale and integer digits, <c>FLOAT</c> to <c>DOUBLE</c>,
///     <c>BOOLEAN</c> to integers, and <c>DATE</c> to the timestamp types. Unknown and nested types are conversions, so a
///     change the engine might accept is routed to a table rebuild rather than failing part-way through a migration.
/// </remarks>
internal static class DuckDBColumnTypePromotion
{
    private enum Family
    {
        Other,
        Signed,
        Unsigned,
        Float,
        Decimal,
        Boolean,
        Date,
        Timestamp,
        TimestampTz,
        Text
    }

    private readonly record struct StoreType(Family Family, string Name, int Width, int Precision, int Scale);

    internal static DuckDBColumnTypeChange Classify(string? oldType, string? newType)
    {
        if (oldType is null || newType is null)
        {
            return string.Equals(oldType, newType, StringComparison.OrdinalIgnoreCase)
                ? DuckDBColumnTypeChange.Unchanged
                : DuckDBColumnTypeChange.Conversion;
        }

        var from = Parse(oldType);
        var to = Parse(newType);
        if (from == to)
        {
            return DuckDBColumnTypeChange.Unchanged;
        }

        return IsWidening(from, to) ? DuckDBColumnTypeChange.Widening : DuckDBColumnTypeChange.Conversion;
    }

    private static bool IsWidening(StoreType from, StoreType to)
        => (from.Family, to.Family) switch
        {
            (Family.Signed, Family.Signed) => to.Width > from.Width,
            (Family.Unsigned, Family.Unsigned) => to.Width > from.Width,
            (Family.Unsigned, Family.Signed) => to.Width > from.Width,
            (Family.Signed or Family.Unsigned, Family.Float) => true,
            (Family.Signed or Family.Unsigned, Family.Decimal) => to.Precision - to.Scale >= IntegerDigits(from),
            (Family.Float, Family.Float) => to.Width > from.Width,
            (Family.Decimal, Family.Float) => true,
            (Family.Decimal, Family.Decimal) => to.Precision >= from.Precision
                && to.Scale >= from.Scale
                && to.Precision - to.Scale >= from.Precision - from.Scale,
            (Family.Boolean, Family.Signed or Family.Unsigned) => true,
            (Family.Date, Family.Timestamp or Family.TimestampTz) => true,
            (Family.Timestamp, Family.TimestampTz) => true,
            _ => false
        };

    private static int IntegerDigits(StoreType type)
        => (type.Family, type.Width) switch
        {
            (_, 1) => 3,
            (_, 2) => 5,
            (_, 4) => 10,
            (Family.Signed, 8) => 19,
            (Family.Unsigned, 8) => 20,
            _ => 39
        };

    private static StoreType Parse(string storeType)
    {
        var normalized = storeType.Trim().ToUpperInvariant();
        var openParen = normalized.IndexOf('(');
        var name = (openParen < 0 ? normalized : normalized[..openParen]).Trim();
        var arguments = openParen < 0 || !normalized.EndsWith(')')
            ? []
            : normalized[(openParen + 1)..^1].Split(',', StringSplitOptions.TrimEntries);

        return name switch
        {
            "TINYINT" or "INT1" => new(Family.Signed, "TINYINT", 1, 0, 0),
            "SMALLINT" or "INT2" or "SHORT" => new(Family.Signed, "SMALLINT", 2, 0, 0),
            "INTEGER" or "INT" or "INT4" or "SIGNED" => new(Family.Signed, "INTEGER", 4, 0, 0),
            "BIGINT" or "INT8" or "LONG" => new(Family.Signed, "BIGINT", 8, 0, 0),
            "HUGEINT" or "INT128" => new(Family.Signed, "HUGEINT", 16, 0, 0),
            "UTINYINT" or "UINT8" => new(Family.Unsigned, "UTINYINT", 1, 0, 0),
            "USMALLINT" or "UINT16" => new(Family.Unsigned, "USMALLINT", 2, 0, 0),
            "UINTEGER" or "UINT32" => new(Family.Unsigned, "UINTEGER", 4, 0, 0),
            "UBIGINT" or "UINT64" => new(Family.Unsigned, "UBIGINT", 8, 0, 0),
            "UHUGEINT" or "UINT128" => new(Family.Unsigned, "UHUGEINT", 16, 0, 0),
            "FLOAT" or "FLOAT4" or "REAL" => new(Family.Float, "FLOAT", 4, 0, 0),
            "DOUBLE" or "FLOAT8" => new(Family.Float, "DOUBLE", 8, 0, 0),
            "DECIMAL" or "NUMERIC" => ParseDecimal(arguments),
            "BOOLEAN" or "BOOL" or "LOGICAL" => new(Family.Boolean, "BOOLEAN", 0, 0, 0),
            "DATE" => new(Family.Date, "DATE", 0, 0, 0),
            "TIMESTAMP" or "DATETIME" or "TIMESTAMP_US" => new(Family.Timestamp, "TIMESTAMP", 0, 0, 0),
            "TIMESTAMPTZ" or "TIMESTAMP WITH TIME ZONE" => new(Family.TimestampTz, "TIMESTAMPTZ", 0, 0, 0),
            // DuckDB ignores VARCHAR length arguments, so every string spelling names the same type.
            "VARCHAR" or "TEXT" or "STRING" or "CHAR" or "BPCHAR" => new(Family.Text, "VARCHAR", 0, 0, 0),
            _ => new(Family.Other, normalized, 0, 0, 0)
        };
    }

    private static StoreType ParseDecimal(string[] arguments)
    {
        // DuckDB's unparameterised DECIMAL is DECIMAL(18,3).
        var precision = arguments.Length > 0 && int.TryParse(arguments[0], NumberStyles.None, CultureInfo.InvariantCulture, out var p)
            ? p
            : 18;
        var scale = arguments.Length > 1 && int.TryParse(arguments[1], NumberStyles.None, CultureInfo.InvariantCulture, out var s)
            ? s
            : arguments.Length > 0 ? 0 : 3;
        return new(Family.Decimal, "DECIMAL", 0, precision, scale);
    }
}
