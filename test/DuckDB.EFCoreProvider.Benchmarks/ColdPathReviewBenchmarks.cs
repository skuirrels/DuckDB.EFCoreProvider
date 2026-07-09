using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;

namespace DuckDB.EFCoreProvider.Benchmarks;

/// <summary>
///     Side-by-side micro-benchmarks for the two remaining review findings, both on cold/cached paths:
///     <list type="number">
///         <item>
///             <description>
///                 Finding #4 — archive-path scheme detection: static <see cref="Regex.IsMatch(string, string)" />
///                 (current) versus a source-generated <see cref="Regex" /> (proposed). Runs during archive
///                 offload, not per row/query.
///             </description>
///         </item>
///         <item>
///             <description>
///                 Finding #3 — the file-source lookup LINQ chain in <c>VisitTable</c> (current) versus a manual
///                 loop (proposed). Runs once per table per query <em>compilation</em>, then cached. Modelled
///                 over a representative single-mapping collection; the annotation lookups are identical in both
///                 variants and therefore excluded so the delta reflects only the removed LINQ iterators.
///             </description>
///         </item>
///     </list>
///     Running current and proposed as separate benchmarks quantifies the per-call delta without a rebuild.
/// </summary>
[MemoryDiagnoser]
public partial class ColdPathReviewBenchmarks
{
    // ---------- Finding #4: archive-path scheme detection ----------

    private const string RemoteArchivePattern = "^[A-Za-z][A-Za-z0-9+.-]*://";
    private const string LocalPath = "/var/data/archive/events";     // no scheme (local filesystem)
    private const string RemotePath = "s3://bucket/archive/events";  // has scheme (remote object store)

    [GeneratedRegex(RemoteArchivePattern)]
    private static partial Regex RemoteArchiveRegex();

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Regex-Local")]
    public bool Regex_Static_Local() => Regex.IsMatch(LocalPath, RemoteArchivePattern);

    [Benchmark]
    [BenchmarkCategory("Regex-Local")]
    public bool Regex_Generated_Local() => RemoteArchiveRegex().IsMatch(LocalPath);

    [Benchmark]
    [BenchmarkCategory("Regex-Remote")]
    public bool Regex_Static_Remote() => Regex.IsMatch(RemotePath, RemoteArchivePattern);

    [Benchmark]
    [BenchmarkCategory("Regex-Remote")]
    public bool Regex_Generated_Remote() => RemoteArchiveRegex().IsMatch(RemotePath);

    // ---------- Finding #3: VisitTable file-source lookup ----------

    // Models tableExpression.Table.EntityTypeMappings (usually one mapping) with no file source configured,
    // which is the common case (the chain scans to the end and returns null). Typed as IEnumerable<> to match
    // the real EF property, so the foreach variant pays the same single boxed-enumerator cost as production.
    private readonly IEnumerable<Mapping> _mappings = new List<Mapping> { new(new EntityLike()) };

    [Benchmark]
    [BenchmarkCategory("VisitTable")]
    public object? VisitTable_Linq()
        => _mappings
            .Select(m => m.TypeBase)
            .OfType<EntityLike>()
            .Select(GetFileSource)
            .FirstOrDefault(s => s is not null);

    [Benchmark]
    [BenchmarkCategory("VisitTable")]
    public object? VisitTable_Loop()
    {
        foreach (var mapping in _mappings)
        {
            if (mapping.TypeBase is EntityLike entity && GetFileSource(entity) is { } source)
            {
                return source;
            }
        }

        return null;
    }

    private static (string Function, string Path)? GetFileSource(EntityLike entity)
        => entity.Function is { Length: > 0 } function && entity.Path is { Length: > 0 } path
            ? (function, path)
            : null;

    private sealed record Mapping(object TypeBase);

    private sealed class EntityLike
    {
        public string? Function { get; init; }
        public string? Path { get; init; }
    }
}
