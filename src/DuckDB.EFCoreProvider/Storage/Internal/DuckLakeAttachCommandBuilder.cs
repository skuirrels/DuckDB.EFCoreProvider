using DuckDB.EFCoreProvider.Infrastructure.Internal;
using System.Text;

namespace DuckDB.EFCoreProvider.Storage.Internal;

internal static class DuckLakeAttachCommandBuilder
{
    public static string Build(DuckLakeOptions profile)
    {
        if (profile.MetadataSource is null
            || !profile.UsesSecret && string.IsNullOrWhiteSpace(profile.MetadataSource))
        {
            throw new InvalidOperationException("The DuckLake metadata source has not been configured.");
        }

        var builder = new StringBuilder();
        builder.Append("ATTACH '")
            .Append(EscapeLiteral("ducklake:" + profile.MetadataSource))
            .Append("' AS ")
            .Append(DelimitIdentifier(profile.CatalogName));

        var parameters = new List<string>();
        if (!profile.CreateIfNotExists)
        {
            parameters.Add("CREATE_IF_NOT_EXISTS false");
        }

        if (!string.IsNullOrWhiteSpace(profile.DataPath))
        {
            parameters.Add($"DATA_PATH '{EscapeLiteral(profile.DataPath)}'");
        }

        if (profile.OverrideDataPath)
        {
            parameters.Add("OVERRIDE_DATA_PATH true");
        }

        if (profile.AutomaticMigration)
        {
            parameters.Add("AUTOMATIC_MIGRATION");
        }

        if (profile.IsReadOnly)
        {
            parameters.Add("READ_ONLY");
        }

        if (parameters.Count > 0)
        {
            builder.Append(" (").AppendJoin(", ", parameters).Append(')');
        }

        builder.Append("; ").Append(BuildUse(profile));
        return builder.ToString();
    }

    public static string BuildUse(DuckLakeOptions profile)
        => $"USE {DelimitIdentifier(profile.CatalogName)};";

    private static string EscapeLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static string DelimitIdentifier(string identifier)
        => '"' + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
}
