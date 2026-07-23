using Microsoft.EntityFrameworkCore.Update;

namespace DuckDB.EFCoreProvider.Update.Internal;

internal enum DuckDBModificationColumnRole
{
    Write,
    Read,
    Condition
}

/// <summary>
///     Shared, allocation-free predicates for classifying compatible modification-command shapes.
/// </summary>
internal static class DuckDBModificationCommandShape
{
    public static bool HasColumns(
        IReadOnlyModificationCommand command,
        DuckDBModificationColumnRole role)
    {
        var modifications = command.ColumnModifications;
        for (var i = 0; i < modifications.Count; i++)
        {
            if (HasRole(modifications[i], role))
            {
                return true;
            }
        }

        return false;
    }

    public static bool AllConditionsAreKeys(IReadOnlyModificationCommand command)
    {
        var modifications = command.ColumnModifications;
        for (var i = 0; i < modifications.Count; i++)
        {
            var modification = modifications[i];
            if (modification.IsCondition && !modification.IsKey)
            {
                return false;
            }
        }

        return true;
    }

    public static bool ColumnNamesEqual(
        IReadOnlyModificationCommand first,
        IReadOnlyModificationCommand second,
        DuckDBModificationColumnRole role)
    {
        var firstModifications = first.ColumnModifications;
        var secondModifications = second.ColumnModifications;
        int firstIndex = 0, secondIndex = 0;

        while (true)
        {
            while (firstIndex < firstModifications.Count && !HasRole(firstModifications[firstIndex], role))
            {
                firstIndex++;
            }

            while (secondIndex < secondModifications.Count && !HasRole(secondModifications[secondIndex], role))
            {
                secondIndex++;
            }

            var firstDone = firstIndex >= firstModifications.Count;
            var secondDone = secondIndex >= secondModifications.Count;
            if (firstDone || secondDone)
            {
                return firstDone && secondDone;
            }

            if (firstModifications[firstIndex].ColumnName != secondModifications[secondIndex].ColumnName)
            {
                return false;
            }

            firstIndex++;
            secondIndex++;
        }
    }

    public static int CountColumns(
        IReadOnlyModificationCommand command,
        DuckDBModificationColumnRole role)
    {
        var modifications = command.ColumnModifications;
        var count = 0;
        for (var i = 0; i < modifications.Count; i++)
        {
            if (HasRole(modifications[i], role))
            {
                count++;
            }
        }

        return count;
    }

    public static bool HasRole(
        IColumnModification modification,
        DuckDBModificationColumnRole role)
        => role switch
        {
            DuckDBModificationColumnRole.Write => modification.IsWrite,
            DuckDBModificationColumnRole.Read => modification.IsRead,
            _ => modification.IsCondition
        };
}