using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
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

    internal static bool IsStructField(IColumnModification modification)
    {
        ArgumentNullException.ThrowIfNull(modification);
        return ResolveFieldInfo(modification) is not null;
    }

    public static bool TryCreate(
        IReadOnlyList<IColumnModification> modifications,
        DuckDBStructMutationMode mode,
        out DuckDBStructMutationPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(modifications);

        // Scalar writes are overwhelmingly the common path. Detect them without allocating the candidate and
        // resolved arrays needed only for STRUCT mutation planning.
        var hasStructField = false;
        for (var i = 0; i < modifications.Count; i++)
        {
            if (ResolveFieldInfo(modifications[i]) is not null)
            {
                hasStructField = true;
                break;
            }
        }

        if (!hasStructField)
        {
            plan = null;
            return false;
        }

        var resolved = new ResolvedModification[modifications.Count];
        for (var ordinal = 0; ordinal < modifications.Count; ordinal++)
        {
            var modification = modifications[ordinal];
            resolved[ordinal] = new ResolvedModification(
                modification.ColumnName,
                modification.ParameterName
                    ?? throw new InvalidOperationException(
                        $"STRUCT write column '{modification.ColumnName}' has no parameter name."),
                ordinal,
                ResolveFieldInfo(modification),
                modification);
        }

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
                group => (Tree: BuildTree(group), IsNull: IsNullRoot(group)),
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
                var (tree, isNull) = grouped[entry.FieldInfo.StructColumnName];
                entries.Add(new DuckDBStructMutationGroup(
                    entry.FieldInfo.StructColumnName,
                    tree,
                    isNull));
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
            var nestedNullStates = ResolveNestedNullStates(modification);
            for (var index = 0; index < field.NestedFieldNames.Count; index++)
            {
                current = current.GetOrAdd(
                    field.NestedFieldNames[index],
                    nestedNullStates[index]);
            }

            current.AddLeaf(
                field.LeafFieldName ?? modification.ColumnName,
                modification.ParameterName,
                modification.ColumnName,
                modification.WriteOrdinal);
        }

        return root.Freeze();
    }

    private static IReadOnlyList<bool> ResolveNestedNullStates(ResolvedModification modification)
    {
        var nestedCount = modification.FieldInfo!.NestedFieldNames.Count;
        if (nestedCount == 0)
        {
            return [];
        }

        if (modification.Modification.Entry is not { } updateEntry
            || modification.Modification.Property?.DeclaringType is not IComplexType declaringType)
        {
            return new bool[nestedCount];
        }

        var complexProperties = new List<IComplexProperty>();
        IComplexType? currentType = declaringType;
        while (currentType?.ComplexProperty is { } complexProperty)
        {
            complexProperties.Add(complexProperty);
            currentType = complexProperty.DeclaringType as IComplexType;
        }

        complexProperties.Reverse();
        var nestedProperties = complexProperties
            .Skip(1)
            .ToArray();
        var states = new bool[nestedCount];
        for (var index = 0; index < states.Length && index < nestedProperties.Length; index++)
        {
            states[index] = updateEntry.GetCurrentValue(nestedProperties[index]) is null;
        }

        return states;
    }

    private static DuckDBStructFieldInfo? ResolveFieldInfo(IColumnModification modification)
        => modification.Column?.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value
                as DuckDBStructFieldInfo
            ?? modification.Property?.GetStructFieldInfo();

    private static bool IsNullRoot(IEnumerable<ResolvedModification> group)
    {
        var entries = group.ToArray();
        var entryRoots = entries
            .Select(entry => ResolveRootComplexProperty(entry.Modification))
            .ToArray();
        if (entryRoots.All(root => root is null))
        {
            return false;
        }

        if (entryRoots.Any(root => root is null))
        {
            throw new InvalidOperationException(
                "A DuckDB STRUCT root cannot combine complex-property fields with standalone field mappings.");
        }

        var roots = entryRoots.Distinct().ToArray();
        if (roots.Length > 1)
        {
            throw new InvalidOperationException(
                "A DuckDB STRUCT root cannot combine fields from different complex properties.");
        }

        var root = roots[0]!;
        foreach (var entry in entries)
        {
            if (entry.Modification.Entry is not { } updateEntry)
            {
                throw new InvalidOperationException(
                    "A DuckDB STRUCT root cannot be evaluated because a write has no owning entity entry.");
            }

            if (updateEntry.GetCurrentValue(root) is not null)
            {
                return false;
            }
        }

        return true;
    }

    private static IComplexProperty? ResolveRootComplexProperty(IColumnModification modification)
    {
        if (modification.Property?.DeclaringType is not IComplexType complexType)
        {
            return null;
        }

        while (complexType.ComplexProperty.DeclaringType is IComplexType parentComplexType)
        {
            complexType = parentComplexType;
        }

        return complexType.ComplexProperty;
    }

    private sealed record ResolvedModification(
        string ColumnName,
        string ParameterName,
        int WriteOrdinal,
        DuckDBStructFieldInfo? FieldInfo,
        IColumnModification Modification);

    private sealed class MutableNode(string? fieldName)
    {
        private readonly List<MutableNode> _children = [];

        public string? FieldName { get; } = fieldName;

        public bool IsNull { get; private set; }

        public string? ParameterName { get; private init; }

        public string? ColumnName { get; private init; }

        public int? WriteOrdinal { get; private init; }

        public MutableNode GetOrAdd(string name, bool isNull)
        {
            var child = _children.FirstOrDefault(
                candidate => string.Equals(candidate.FieldName, name, StringComparison.OrdinalIgnoreCase));
            if (child is null)
            {
                child = new MutableNode(name)
                {
                    IsNull = isNull
                };
                _children.Add(child);
            }
            else if (child.IsNull != isNull)
            {
                throw new InvalidOperationException(
                    $"DuckDB STRUCT nested field '{name}' has conflicting null states in one mutation.");
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
                IsNull,
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
    DuckDBStructMutationNode Root,
    bool IsNull)
    : DuckDBStructMutationEntry(StructColumnName)
{
    public override bool HasSamePhysicalShape(DuckDBStructMutationEntry other)
        => other is DuckDBStructMutationGroup group
            && string.Equals(StructColumnName, group.StructColumnName, StringComparison.OrdinalIgnoreCase)
            && IsNull == group.IsNull
            && Root.HasSamePhysicalShape(group.Root);
}

internal sealed class DuckDBStructMutationNode
{
    public DuckDBStructMutationNode(
        string? fieldName,
        bool isNull,
        string? parameterName,
        string? columnName,
        int? writeOrdinal,
        IEnumerable<DuckDBStructMutationNode> children)
    {
        FieldName = fieldName;
        IsNull = isNull;
        ParameterName = parameterName;
        ColumnName = columnName;
        WriteOrdinal = writeOrdinal;
        Children = children.ToImmutableArray();
    }

    public string? FieldName { get; }

    public bool IsNull { get; }

    public string? ParameterName { get; }

    public string? ColumnName { get; }

    public int? WriteOrdinal { get; }

    public IReadOnlyList<DuckDBStructMutationNode> Children { get; }

    public bool IsLeaf => ParameterName is not null;

    public bool HasSamePhysicalShape(DuckDBStructMutationNode other)
        => string.Equals(FieldName, other.FieldName, StringComparison.OrdinalIgnoreCase)
            && IsNull == other.IsNull
            && IsLeaf == other.IsLeaf
            && Children.Count == other.Children.Count
            && Children.Zip(other.Children).All(pair => pair.First.HasSamePhysicalShape(pair.Second));
}