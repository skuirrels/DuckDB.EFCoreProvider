namespace Microsoft.EntityFrameworkCore.TestUtilities;

/// <summary>
///     Helpers for asserting on captured <c>SaveChanges</c> command text when merged update and delete runs are
///     preceded by their row check.
/// </summary>
internal static class BatchedCommandText
{
    private const string RowCheckPrefix = "SELECT CAST(COUNT(*) AS INTEGER) FROM (SELECT 1 FROM ";

    /// <summary>Returns the command text without a leading merged-run row check, if it has one.</summary>
    public static string WithoutRowCheck(string commandText)
    {
        if (!commandText.StartsWith(RowCheckPrefix, StringComparison.Ordinal))
        {
            return commandText;
        }

        var end = commandText.IndexOf(';', StringComparison.Ordinal);
        return end < 0 ? commandText : commandText[(end + 1)..].TrimStart();
    }
}