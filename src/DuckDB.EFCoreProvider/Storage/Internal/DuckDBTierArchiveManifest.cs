using DuckDB.EFCoreProvider.Extensions;

namespace DuckDB.EFCoreProvider.Storage.Internal;

internal sealed class DuckDBTierArchiveManifest
{
    private readonly DuckDBTierAggregate _aggregate;
    private readonly TierArchiveOperation _operation;
    private readonly DateTime? _previousWatermark;
    private readonly DateTime _windowStart;
    private readonly DateTime _windowEnd;
    private readonly string? _revision;
    private readonly TierManifestOptions _manifestOptions;
    private readonly Dictionary<DuckDBTierNode, NodeProgress> _nodes;

    public DuckDBTierArchiveManifest(
        DuckDBTierAggregate aggregate,
        TierArchiveOperation operation,
        DateTime? previousWatermark,
        DateTime windowStart,
        DateTime windowEnd,
        string activeArchiveBasePath,
        string? revision,
        TierManifestOptions? manifestOptions = null)
    {
        _aggregate = aggregate;
        _operation = operation;
        _previousWatermark = previousWatermark;
        _windowStart = windowStart;
        _windowEnd = windowEnd;
        _revision = revision;
        _manifestOptions = manifestOptions ?? TierManifestOptions.Default;
        _manifestOptions.Validate();
        _nodes = aggregate.Nodes.ToDictionary(
            node => node,
            node => new NodeProgress(NodeArchivePath(activeArchiveBasePath, node.Table)));
    }

    public string ArchivePath(DuckDBTierNode node) => _nodes[node].ArchivePath;

    public TierManifestOptions ManifestOptions => _manifestOptions;

    public void SetSelected(DuckDBTierNode node, long rows) => _nodes[node].SelectedRows = rows;

    public long SelectedRows(DuckDBTierNode node) => _nodes[node].SelectedRows;

    public void SetCopied(DuckDBTierNode node, long rows, IReadOnlyList<string> files)
    {
        _nodes[node].CopiedRows = rows;
        _nodes[node].Files = files;
        _nodes[node].FileCount = files.Count;
    }

    public void SetCopied(DuckDBTierNode node, long rows, DuckDBArchiveFileSummary summary)
    {
        _nodes[node].CopiedRows = rows;
        SetFileSummary(node, summary);
    }

    public void SetFiles(DuckDBTierNode node, IReadOnlyList<string> files)
    {
        _nodes[node].Files = files;
        _nodes[node].FileCount = files.Count;
    }

    public void SetFileSummary(DuckDBTierNode node, DuckDBArchiveFileSummary summary)
    {
        _nodes[node].Files = summary.Files;
        _nodes[node].FileCount = summary.FileCount;
        _nodes[node].TotalBytes = summary.TotalBytes;
        _nodes[node].FilesTruncated = summary.IsTruncated;
    }

    public void AddDeleted(DuckDBTierNode node, long rows) => _nodes[node].DeletedRows += rows;

    public TierArchiveResult Build(DateTime watermark, bool noOp, TierArchiveStage stage)
    {
        var root = _nodes[_aggregate.Root];
        return new TierArchiveResult(root.SelectedRows, watermark, RedactCredentials(root.ArchivePath), noOp)
        {
            Binding = new TieredStorageBindingInfo(
                _aggregate.BindingId,
                _aggregate.Root.Entity.ClrType,
                _aggregate.ControlKey),
            Operation = _operation,
            PreviousWatermark = _previousWatermark,
            WindowStart = _windowStart,
            WindowEnd = _windowEnd,
            Revision = _revision,
            Stage = stage,
            Nodes = _aggregate.Nodes.Select(node =>
            {
                var progress = _nodes[node];
                return new TierArchiveNodeResult(
                    node.Table,
                    node.Schema,
                    progress.SelectedRows,
                    progress.CopiedRows,
                    progress.DeletedRows,
                    RedactCredentials(progress.ArchivePath),
                    progress.Files.Select(RedactCredentials).ToArray())
                {
                    BindingId = node.BindingId,
                    FileCount = progress.FileCount,
                    TotalBytes = progress.TotalBytes,
                    FilesTruncated = progress.FilesTruncated,
                };
            }).ToArray(),
        };
    }

    public static string NodeArchivePath(string archiveBasePath, string table)
        => archiveBasePath.TrimEnd('/', '\\') + "/" + table;

    internal static string RedactCredentials(string path)
    {
        var schemeSeparator = path.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator < 0)
        {
            return path;
        }

        var query = path.IndexOfAny(['?', '#'], schemeSeparator + 3);
        var withoutQuery = query < 0 ? path : path[..query];
        var authorityStart = schemeSeparator + 3;
        var authorityEnd = withoutQuery.IndexOf('/', authorityStart);
        if (authorityEnd < 0)
        {
            authorityEnd = withoutQuery.Length;
        }

        var userInfo = withoutQuery.LastIndexOf('@', authorityEnd - 1, authorityEnd - authorityStart);
        return userInfo < authorityStart
            ? withoutQuery
            : withoutQuery[..authorityStart] + withoutQuery[(userInfo + 1)..];
    }

    private sealed class NodeProgress(string archivePath)
    {
        public string ArchivePath { get; set; } = archivePath;
        public long SelectedRows { get; set; }
        public long CopiedRows { get; set; }
        public long DeletedRows { get; set; }
        public long FileCount { get; set; }
        public long TotalBytes { get; set; }
        public bool FilesTruncated { get; set; }
        public IReadOnlyList<string> Files { get; set; } = [];
    }
}
