using DuckDB.NET.Native;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DuckDB.EFCoreProvider.Internal;

/// <summary>
///     Resolves the version of the DuckDB native library loaded into the process.
/// </summary>
/// <remarks>
///     Only the composition boundary reads this value: <see cref="DuckDBEngineCapabilities" /> translates it into
///     capabilities, and every other component branches on those capabilities instead of the version.
/// </remarks>
internal static partial class DuckDBNativeEngine
{
    /// <summary>The engine generation that the provider treats as the baseline when the version is unknown.</summary>
    internal static readonly Version Baseline = new(1, 5, 0);

    /// <summary>The first engine generation with the DuckDB 2.0 <c>ALTER TABLE</c> and sequence capabilities.</summary>
    internal static readonly Version Cyanoptera = new(2, 0, 0);

    private static readonly Lazy<Version> LoadedVersion = new(ResolveLoadedVersion);

    /// <summary>The version of the loaded native library, or <see cref="Baseline" /> when it cannot be read.</summary>
    internal static Version Version => LoadedVersion.Value;

    /// <summary>
    ///     Parses a DuckDB library version such as <c>v1.5.6</c> or <c>v2.0.0-0-g9a97642349</c>.
    /// </summary>
    internal static Version Parse(string? libraryVersion)
    {
        var match = libraryVersion is null ? null : LibraryVersionPattern().Match(libraryVersion);
        return match is { Success: true }
            ? new Version(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture))
            : Baseline;
    }

    private static Version ResolveLoadedVersion()
    {
        try
        {
            return Parse(NativeMethods.Startup.DuckDBLibraryVersion());
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            // Model building and design-time services can run without a loadable native library. The baseline
            // capabilities keep their behaviour unchanged, and any real command fails later with the load error.
            return Baseline;
        }
    }

    [GeneratedRegex(@"^v?(\d+)\.(\d+)\.(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex LibraryVersionPattern();
}
