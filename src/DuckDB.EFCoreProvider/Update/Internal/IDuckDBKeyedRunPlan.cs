namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     A merged run of <c>SaveChanges</c> updates or deletes that each target one row of the same table by its
///     original key values.
/// </summary>
internal interface IDuckDBKeyedRunPlan
{
    string TableName { get; }

    string? Schema { get; }

    int RowCount { get; }

    int KeyColumnCount { get; }

    string GetKeyColumnName(int keyIndex);

    string GetOriginalKeyParameterName(int rowIndex, int keyIndex);
}