namespace DuckDB.EFCoreProvider.Metadata.Internal;

internal static class DuckDBValueGenerationStrategyCompatibility
{
    public static bool IsAutoIncrementCompatible(Type clrType)
    {
        clrType = clrType.UnwrapNullableType();

        return clrType == typeof(byte)
               || clrType == typeof(short)
               || clrType == typeof(int)
               || clrType == typeof(long)
               || clrType == typeof(sbyte)
               || clrType == typeof(ushort)
               || clrType == typeof(uint)
               || clrType == typeof(ulong);
    }
}
