using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DuckDB.EFCoreProvider.Extensions;

public static partial class DuckDBArchiveExtensions
{
    private const string CandidateMarkerFileName = "_duckdb_tier_candidate.json";

    private static async Task RegisterRemoteArchiveCandidateAsync(
        DuckDBConnection connection,
        DuckDBTierAggregate aggregate,
        string generationId,
        string replacementBasePath,
        TierArchiveOperation operation,
        CancellationToken cancellationToken)
    {
        if (!IsRemoteArchive(aggregate.ArchiveBasePath))
        {
            return;
        }

        var expected = CreateCandidateMarker(aggregate, generationId, operation);
        var markerPath = CandidateMarkerPath(replacementBasePath);
        var existing = await TryReadCandidateMarkerAsync(connection, markerPath, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (!CandidateMarkersMatch(existing, expected, includeStartedAt: false))
            {
                throw new InvalidOperationException(
                    $"Remote archive candidate '{generationId}' already has a marker for a different Provider "
                    + "binding or contract. The candidate was not overwritten.");
            }

            return;
        }

        var json = JsonSerializer.Serialize(expected);
        var commandText = "COPY (SELECT " + SqlString(json) + " AS marker_json) TO "
                          + SqlString(markerPath) + " (FORMAT JSON);";
        await ExecuteNonQueryAsync(connection, commandText, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AddRemoteUnpublishedCandidatesAsync(
        DuckDBConnection connection,
        IDuckDBArchiveFileProbe archiveFileProbe,
        DuckDBTierAggregate aggregate,
        DateTime watermark,
        string activeGenerationId,
        IReadOnlySet<string> recordedIds,
        int representativeFiles,
        bool hasControlMetadata,
        bool hasGenerationCatalogue,
        ICollection<TierArchiveGenerationInfo> generations,
        CancellationToken cancellationToken)
    {
        var revisionBasePath = aggregate.ArchiveBasePath.TrimEnd('/', '\\') + "/_revisions/";
        var markerGlob = revisionBasePath + "*/" + CandidateMarkerFileName;
        var parquetGlob = revisionBasePath + "*/**/*.parquet";
        var paths = await ReadGlobPathsAsync(connection, [markerGlob, parquetGlob], cancellationToken)
            .ConfigureAwait(false);
        var generationIds = paths
            .Select(path => TryReadGenerationId(path, revisionBasePath))
            .Where(id => id is not null)
            .Cast<string>()
            .Where(id => id != activeGenerationId && !recordedIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (var generationId in generationIds)
        {
            var candidateBasePath = revisionBasePath + generationId;
            var marker = await TryReadCandidateMarkerAsync(
                    connection,
                    CandidateMarkerPath(candidateBasePath),
                    cancellationToken)
                .ConfigureAwait(false);
            var expected = marker is null
                ? null
                : CreateCandidateMarker(aggregate, generationId, marker.Operation, marker.StartedAtUtc);
            var compatible = marker is not null
                             && expected is not null
                             && CandidateMarkersMatch(marker, expected, includeStartedAt: true);
            var safelyClassified = hasControlMetadata && hasGenerationCatalogue && compatible;
            var nodeEvidence = new List<TierArchiveGenerationNodeInfo>();
            var representative = new List<string>();
            long fileCount = 0;
            long totalBytes = 0;

            if (compatible)
            {
                foreach (var node in aggregate.Nodes)
                {
                    var nodePath = DuckDBTierArchiveManifest.NodeArchivePath(candidateBasePath, node.Table);
                    var summary = archiveFileProbe.HasArchiveFiles(connection, nodePath)
                        ? archiveFileProbe.GetArchiveFileSummary(
                            connection,
                            nodePath,
                            new TierManifestOptions
                            {
                                Detail = TierManifestDetail.RepresentativeFiles,
                                MaxFilesPerNode = representativeFiles,
                            })
                        : new DuckDBArchiveFileSummary(0, 0, [], IsTruncated: false);
                    var redactedFiles = summary.Files
                        .Select(DuckDBTierArchiveManifest.RedactCredentials)
                        .ToArray();
                    fileCount += summary.FileCount;
                    totalBytes += summary.TotalBytes;
                    representative.AddRange(
                        redactedFiles.Take(Math.Max(0, representativeFiles - representative.Count)));
                    nodeEvidence.Add(new TierArchiveGenerationNodeInfo(
                        node.Table,
                        node.Schema,
                        node.BindingId,
                        summary.FileCount,
                        summary.TotalBytes,
                        redactedFiles));
                }
            }
            else if (archiveFileProbe.HasArchiveFiles(connection, candidateBasePath))
            {
                var summary = archiveFileProbe.GetArchiveFileSummary(
                    connection,
                    candidateBasePath,
                    new TierManifestOptions
                    {
                        Detail = TierManifestDetail.RepresentativeFiles,
                        MaxFilesPerNode = representativeFiles,
                    });
                fileCount = summary.FileCount;
                totalBytes = summary.TotalBytes;
                representative.AddRange(
                    summary.Files.Select(DuckDBTierArchiveManifest.RedactCredentials));
            }

            generations.Add(new TierArchiveGenerationInfo(
                generationId,
                safelyClassified
                    ? TierArchiveGenerationState.UnpublishedCandidate
                    : TierArchiveGenerationState.Unknown,
                DuckDBTierArchiveManifest.RedactCredentials(candidateBasePath),
                watermark,
                marker?.StartedAtUtc ?? DateTime.MinValue,
                fileCount,
                totalBytes,
                representative)
            {
                Operation = marker?.Operation,
                HasCandidateMarker = marker is not null,
                ContractCompatible = compatible,
                Nodes = nodeEvidence,
                ProviderArchivePath = candidateBasePath,
            });
        }
    }

    private static RemoteArchiveCandidateMarker CreateCandidateMarker(
        DuckDBTierAggregate aggregate,
        string generationId,
        TierArchiveOperation operation,
        DateTime? startedAtUtc = null)
        => new()
        {
            Version = 1,
            ControlKey = aggregate.ControlKey,
            RootBindingId = aggregate.BindingId,
            GenerationId = generationId,
            Operation = operation,
            StartedAtUtc = startedAtUtc ?? DateTime.UtcNow,
            ArchiveContractSha256 = Sha256(aggregate.ArchiveSpec),
            PartitionContractSha256 = Sha256(aggregate.PartitionSpec),
            Nodes = aggregate.Nodes.Select(node => new RemoteArchiveCandidateNode
            {
                Table = node.Table,
                Schema = node.Schema,
                BindingId = node.BindingId,
            }).ToArray(),
        };

    private static bool CandidateMarkersMatch(
        RemoteArchiveCandidateMarker actual,
        RemoteArchiveCandidateMarker expected,
        bool includeStartedAt)
        => actual.Nodes is not null
           && Enum.IsDefined(actual.Operation)
           && actual.Version == expected.Version
           && actual.ControlKey == expected.ControlKey
           && actual.RootBindingId == expected.RootBindingId
           && actual.GenerationId == expected.GenerationId
           && actual.Operation == expected.Operation
           && (!includeStartedAt || actual.StartedAtUtc == expected.StartedAtUtc)
           && actual.ArchiveContractSha256 == expected.ArchiveContractSha256
           && actual.PartitionContractSha256 == expected.PartitionContractSha256
           && actual.Nodes.SequenceEqual(expected.Nodes);

    private static async Task<RemoteArchiveCandidateMarker?> TryReadCandidateMarkerAsync(
        DuckDBConnection connection,
        string markerPath,
        CancellationToken cancellationToken)
    {
        var separator = markerPath.LastIndexOf('/');
        var markerGlob = separator < 0 ? markerPath : markerPath[..(separator + 1)] + "*";
        var markerExists = (await ReadGlobPathsAsync(connection, [markerGlob], cancellationToken)
                .ConfigureAwait(false))
            .Any(path => string.Equals(path, markerPath, StringComparison.Ordinal));
        if (!markerExists)
        {
            return null;
        }

        try
        {
            var value = await ExecuteScalarAsync(
                    connection,
                    "SELECT content FROM read_text(" + SqlString(markerPath) + ") LIMIT 1;",
                    cancellationToken)
                .ConfigureAwait(false);
            if (value is null or DBNull)
            {
                return null;
            }

            using var envelope = JsonDocument.Parse(Convert.ToString(value)!);
            var markerJson = envelope.RootElement.GetProperty("marker_json").GetString();
            return markerJson is null
                ? null
                : JsonSerializer.Deserialize<RemoteArchiveCandidateMarker>(markerJson);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<string>> ReadGlobPathsAsync(
        DuckDBConnection connection,
        IReadOnlyList<string> globs,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = string.Join(
            " UNION ALL ",
            globs.Select(glob => "SELECT file FROM glob(" + SqlString(glob) + ")")) + ";";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var paths = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            paths.Add(reader.GetString(0));
        }

        return paths;
    }

    private static string? TryReadGenerationId(string path, string revisionBasePath)
    {
        var start = path.IndexOf(revisionBasePath, StringComparison.Ordinal);
        if (start < 0)
        {
            var marker = "/_revisions/";
            start = path.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            start += marker.Length;
        }
        else
        {
            start += revisionBasePath.Length;
        }

        var end = path.IndexOf('/', start);
        return end <= start ? null : path[start..end];
    }

    private static string CandidateMarkerPath(string replacementBasePath)
        => replacementBasePath.TrimEnd('/', '\\') + "/" + CandidateMarkerFileName;

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string SqlString(string value) => "'" + value.Replace("'", "''") + "'";

    private sealed record RemoteArchiveCandidateMarker
    {
        public int Version { get; init; }
        public required string ControlKey { get; init; }
        public required string RootBindingId { get; init; }
        public required string GenerationId { get; init; }
        public TierArchiveOperation Operation { get; init; }
        public DateTime StartedAtUtc { get; init; }
        public required string ArchiveContractSha256 { get; init; }
        public required string PartitionContractSha256 { get; init; }
        public required IReadOnlyList<RemoteArchiveCandidateNode> Nodes { get; init; }
    }

    private sealed record RemoteArchiveCandidateNode
    {
        public required string Table { get; init; }
        public string? Schema { get; init; }
        public required string BindingId { get; init; }
    }
}