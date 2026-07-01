using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.CodeAnalysis;
using System.Reflection;

namespace Microsoft.EntityFrameworkCore.TestUtilities;

public class DuckDBPrecompiledQueryTestHelpers : PrecompiledQueryTestHelpers
{
    public static readonly DuckDBPrecompiledQueryTestHelpers Instance = new();

    protected override IEnumerable<MetadataReference> BuildProviderMetadataReferences()
    {
        yield return MetadataReference.CreateFromFile(typeof(DuckDBOptionsExtension).Assembly.Location);
        yield return MetadataReference.CreateFromFile(Assembly.GetExecutingAssembly().Location);
    }
}
