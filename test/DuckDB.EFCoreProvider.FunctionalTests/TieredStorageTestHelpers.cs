using System.Data;
using System.Text;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

internal static class TieredStorageTestHelpers
{
    public static string Explain<T>(DbContext context, IQueryable<T> query)
    {
        using var command = query.CreateDbCommand();
        command.CommandText = "EXPLAIN ANALYZE " + command.CommandText;
        var openedHere = command.Connection!.State != ConnectionState.Open;
        if (openedHere)
        {
            context.Database.OpenConnection();
        }

        try
        {
            using var reader = command.ExecuteReader();
            var plan = new StringBuilder();
            while (reader.Read())
            {
                for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                {
                    plan.AppendLine(reader.GetValue(ordinal)?.ToString());
                }
            }

            return plan.ToString();
        }
        finally
        {
            if (openedHere)
            {
                context.Database.CloseConnection();
            }
        }
    }

    public static void AssertFilesPruned(string plan, string expectedFraction)
    {
        Assert.Contains("Scanning Files:", plan);
        Assert.Contains(expectedFraction, plan);
    }
}