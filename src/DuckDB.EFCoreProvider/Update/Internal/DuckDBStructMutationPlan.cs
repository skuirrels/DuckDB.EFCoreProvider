using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Update;
using System.Collections.Immutable;

namespace DuckDB.EFCoreProvider.Update.Internal;

internal enum DuckDBStructMutationMode
{
    Insert,
    Update,
    BulkUpdate
}

/// <summary>
///     Resolves STRUCT leaf modifications into an immutable ordered rendering plan.
/// </summary>
internal sealed class DuckDBStructMutationPlan
{
    private DuckDBStructMutationPlan(
        DuckDBStructMutationMode mode,
        IEnumerable<DuckDBStructMutationEntry> entries)
    {
        Mode = mode;
        Entries = entries.ToImmutableArray();
    }

    public DuckDBStructMutationMode Mode { get; }

    public IReadOnlyList<DuckDBStructMutationEntry> Entries { get; }

    public static bool TryCreate(
        IReadOnlyList<IColumnModification> modifications,
        DuckDBStructMutationMode mode,
        out DuckDBStructMutationPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(modifications);

        var candidates = modifications
            .Select((modification, ordinal) => new
            {
                Modification = modification,
                Ordinal = ordinal,
                FieldInfo = ResolveFieldInfo(modification)
            })
            .ToArray();
        if (candidates.All(entry => entry.FieldInfo is null))
        {
            plan = null;
            return false;
        }

        var resolved = candidates
            .Select(entry => new ResolvedModification(
                entry.Modification.ColumnName,
                entry.Modification.ParameterName
                    ?? throw new InvalidOperationException(
                        $"STRUCT write column '{entry.Modification.ColumnName}' has no parameter name."),
                entry.Ordinal,
                entry.FieldInfo))
            .ToArray();

                    var collision = DuckDBStructPathCollision.Find(
                        resolved
                            .Where(entry => entry.FieldInfo is not null)
                            .Select(entry => entry.FieldInfo!));
                    if (collision is { } conflict)
                    {
                        var (root, leafPath, nestedPath) = conflict;
                        throw new InvalidOperationException(
                            $"DuckDB STRUCT root '{root}' has conflicting write paths: "
                            + $"'{DuckDBStructPathCollision.FormatPath(root, leafPath)}' is used as a scalar leaf "
                            + $"and as a parent of '{DuckDBStructPathCollision.FormatPath(root, nestedPath)}'.");
                    }

                    var grouped = resolved
            .Where(entry => entry.FieldInfo is not null)
            .GroupBy(entry => entry.FieldInfo!.StructColumnName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => BuildTree(group),
                StringComparer.OrdinalIgnoreCase);

        var entries = new List<DuckDBStructMutationEntry>(resolved.Length);
        var emittedStructColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in resolved)
        {
            if (entry.FieldInfo is null)
            {
                entries.Add(new DuckDBStandaloneMutationEntry(
                    entry.ColumnName,
                    entry.ParameterName,
                    entry.WriteOrdinal));
            }
            else if (emittedStructColumns.Add(entry.FieldInfo.StructColumnName))
            {
                entries.Add(new DuckDBStructMutationGroup(
                    entry.FieldInfo.StructColumnName,
                    grouped[entry.FieldInfo.StructColumnName]));
            }
        }

        plan = new DuckDBStructMutationPlan(mode, entries);
        return true;
    }

    public bool HasSamePhysicalShape(DuckDBStructMutationPlan other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Mode == other.Mode
            && Entries.Count == other.Entries.Count
            && Entries.Zip(other.Entries).All(pair => pair.First.HasSamePhysicalShape(pair.Second));
    }

    private static DuckDBStructMutationNode BuildTree(IEnumerable<ResolvedModification> modifications)
    {
        var root = new MutableNode(null);
        foreach (var modification in modifications)
        {
            var field = modification.FieldInfo!;
            var current = root;
            foreach (var nestedName in field.NestedFieldNames)
            {
                current = current.GetOrAdd(nestedName);
            }

            current.AddLeaf(
                field.LeafFieldName ?? modification.ColumnName,
                modification.ParameterName,
                modification.ColumnName,
                modification.WriteOrdinal);
        }

        return root.Freeze();
    }

    private static DuckDBStructFieldInfo? ResolveFieldInfo(IColumnModification modification)
        => modification.Column?.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value
                as DuckDBStructFieldInfo
            ?? modification.Property?.GetStructFieldInfo();

    private sealed record ResolvedModification(
        string ColumnName,
        string ParameterName,
        int WriteOrdinal,
        DuckDBStructFieldInfo? FieldInfo);

    private sealed class MutableNode(string? fieldName)
    {
        private readonly List<MutableNode> _children = [];

        public string? FieldName { get; } = fieldName;

        public string? ParameterName { get; private init; }

        public string? ColumnName { get; private init; }

        public int? WriteOrdinal { get; private init; }

        public MutableNode GetOrAdd(string name)
        {
            var child = _children.FirstOrDefault(
                candidate => string.Equals(candidate.FieldName, name, StringComparison.OrdinalIgnoreCase));
            if (child is null)
            {
                child = new MutableNode(name);
                _children.Add(child);
            }

            return child;
        }

        public void AddLeaf(string name, string parameterName, string columnName, int writeOrdinal)
        {
            if (_children.Any(candidate => string.Equals(
                    candidate.FieldName,
                    name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Duplicate DuckDB STRUCT field '{name}' in one mutation.");
            }

            _children.Add(new MutableNode(name)
            {
                ParameterName = parameterName,
                ColumnName = columnName,
                WriteOrdinal = writeOrdinal
            });
        }

        public DuckDBStructMutationNode Freeze()
            => new(
                FieldName,
                ParameterName,
                ColumnName,
                WriteOrdinal,
                _children.Select(child => child.Freeze()));
    }
}

internal abstract record DuckDBStructMutationEntry(string ColumnName)
{
    public abstract bool HasSamePhysicalShape(DuckDBStructMutationEntry other);
}

internal sealed record DuckDBStandaloneMutationEntry(
    string PhysicalColumnName,
    string ParameterName,
    int WriteOrdinal)
    : DuckDBStructMutationEntry(PhysicalColumnName)
{
    public override bool HasSamePhysicalShape(DuckDBStructMutationEntry other)
        => other is DuckDBStandaloneMutationEntry standalone
            && string.Equals(ColumnName, standalone.ColumnName, StringComparison.Ordinal);
}

internal sealed record DuckDBStructMutationGroup(
    string StructColumnName,
    DuckDBStructMutationNode Root)
    : DuckDBStructMutationEntry(StructColumnName)
{
    public override bool HasSamePhysicalShape(DuckDBStructMutationEntry other)
        => other is DuckDBStructMutationGroup group
            && string.Equals(StructColumnName, group.StructColumnName, StringComparison.OrdinalIgnoreCase)
            && Root.HasSamePhysicalShape(group.Root);
}

internal sealed class DuckDBStructMutationNode
{
    public DuckDBStructMutationNode(
        string? fieldName,
        string? parameterName,
        string? columnName,
        int? writeOrdinal,
        IEnumerable<DuckDBStructMutationNode> children)
    {
        FieldName = fieldName;
        ParameterName = parameterName;
        ColumnName = columnName;
        WriteOrdinal = writeOrdinal;
        Children = children.ToImmutableArray();
    }

    public string? FieldName { get; }

    public string? ParameterName { get; }

    public string? ColumnName { get; }

    public int? WriteOrdinal { get; }

    public IReadOnlyList<DuckDBStructMutationNode> Children { get; }

    public bool IsLeaf => ParameterName is not null;

    public bool HasSamePhysicalShape(DuckDBStructMutationNode other)
        => string.Equals(FieldName, other.FieldName, StringComparison.OrdinalIgnoreCase)
            && IsLeaf == other.IsLeaf
            && Children.Count == other.Children.Count
            && Children.Zip(other.Children).All(pair => pair.First.HasSamePhysicalShape(pair.Second));
}