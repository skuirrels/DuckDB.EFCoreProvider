using System.Globalization;
using System.Text.RegularExpressions;

namespace DuckDB.EFCoreProvider.Infrastructure.Internal;

/// <summary>
///     Validates the storage and DuckLake specification versions accepted by the provider options.
/// </summary>
/// <remarks>
///     Only the shape is checked here. The loaded engine owns the list of versions it can write, so an unknown but
///     well-formed version is rejected by DuckDB when the database is opened or attached.
/// </remarks>
internal static partial class DuckDBStorageVersions
{
    /// <summary>The DuckDB configuration key that selects the storage format written to new database files.</summary>
    public const string StorageCompatibilityVersionKey = "storage_compatibility_version";

    /// <summary>The storage version keyword that always writes the engine's newest format.</summary>
    public const string Latest = "latest";

    /// <summary>The oldest DuckLake specification that DuckLake accepts as a writable <c>DUCKLAKE_VERSION</c>.</summary>
    private static readonly Version MinimumWritableDuckLakeVersion = new(1, 0);

    /// <summary>
    ///     The oldest storage version that keeps DuckDB's current encryption scheme. DuckDB writes an encrypted file
    ///     with an explicit storage version below it using the legacy scheme, and rejects versions below v1.4.0.
    /// </summary>
    public static readonly Version MinimumEncryptedStorageVersion = new(1, 5, 0);

    /// <summary>
    ///     Validates a DuckDB storage compatibility version such as <c>v1.5.0</c>, or <c>latest</c>, and returns it in
    ///     the lower-case form DuckDB accepts. DuckDB matches these values case-sensitively, so every later
    ///     comparison, including against a connection-string value, is ordinal.
    /// </summary>
    public static string NormalizeStorageCompatibilityVersion(string version, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version, parameterName);
        var normalized = version.Trim().ToLowerInvariant();
        if (!string.Equals(normalized, Latest, StringComparison.Ordinal) && !StorageVersionPattern().IsMatch(normalized))
        {
            throw new ArgumentException(
                $"'{version}' is not a DuckDB storage version. Use a release version such as 'v1.5.0', or 'latest'.",
                parameterName);
        }

        return normalized;
    }

    /// <summary>
    ///     Returns the release a normalized storage version names, or <see langword="null" /> for <c>latest</c>.
    /// </summary>
    public static Version? GetRelease(string normalizedVersion)
        => string.Equals(normalizedVersion, Latest, StringComparison.Ordinal)
            ? null
            : Version.Parse(normalizedVersion.AsSpan(1));

    /// <summary>
    ///     Validates a writable DuckLake specification version such as <c>1.0</c> or <c>1.1-dev1</c>, and returns
    ///     it in the lower-case form DuckLake matches exactly.
    /// </summary>
    public static string NormalizeDuckLakeVersion(string version, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version, parameterName);
        var normalized = version.Trim().ToLowerInvariant();
        var match = DuckLakeVersionPattern().Match(normalized);
        if (!match.Success)
        {
            throw new ArgumentException(
                $"'{version}' is not a DuckLake specification version. Use a version such as '1.0' or '1.1-dev1'.",
                parameterName);
        }

        var release = new Version(
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
        if (release < MinimumWritableDuckLakeVersion)
        {
            throw new ArgumentException(
                $"DuckLake can only write specification 1.0 or later, but '{version}' was configured.",
                parameterName);
        }

        return normalized;
    }

    [GeneratedRegex(@"^v\d+\.\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex StorageVersionPattern();

    [GeneratedRegex(@"^(\d+)\.(\d+)(?:-dev\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DuckLakeVersionPattern();
}
