namespace DuckDB.EFCoreProvider.Infrastructure.Internal;

internal static class DuckLakeMetadataSourceValidator
{
    public static void ValidateLocalPath(string metadataPath, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataPath, parameterName);
        if (HasUriScheme(metadataPath))
        {
            throw new ArgumentException(
                "DuckLake local metadata sources must be file paths. Configure remote metadata and credentials "
                + "through a named-secret profile on a caller-initialized connection.",
                parameterName);
        }
    }

    private static bool HasUriScheme(string value)
    {
        var separator = value.IndexOf(':');
        if (separator <= 0)
        {
            return false;
        }

        if (separator == 1
            && char.IsAsciiLetter(value[0])
            && value.Length > 2
            && value[2] is '/' or '\\')
        {
            return false;
        }

        if (!char.IsAsciiLetter(value[0]))
        {
            return false;
        }

        for (var index = 1; index < separator; index++)
        {
            var character = value[index];
            if (!char.IsAsciiLetterOrDigit(character) && character is not '+' and not '-' and not '.')
            {
                return false;
            }
        }

        return true;
    }
}