using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Update;
using System.Collections.Immutable;

namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     Resolves STRUCT leaf modifications into an immutable ordered mutation shape.
/// </summary>
internal sealed class DuckDBStructMutationPlan
{
    private DuckDBStructMutationPlan(IReadOnlyList<DuckDBStructMutationEntry> entries)
        => Entries = entries.ToImmutableArray();

    public IReadOnlyList<DuckDBStructMutationEntry> Entries { get; }

    public static bool TryCreate(
        IReadOnlyList<IColumnModification> modifications,
        string tableName,
        string? schema,
        out DuckDBStructMutationPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(modifications);

        var columnMap = ResolveStructColumnMap(modifications, tableName, schema);
        var groups = new Dictionary<string, List<(DuckDBStructFieldInfo FieldInfo, IColumnModification Modification)>>();

        foreach (var modification in modifications)
        {
            if (TryGetStructFieldInfo(modification, columnMap) is { } fieldInfo)
            {
                if (!groups.TryGetValue(fieldInfo.StructColumnName, out var fields))
                {
                    fields = [];
                    groups.Add(fieldInfo.StructColumnName, fields);
                }

                fields.Add((fieldInfo, modification));
            }
        }

        if (groups.Count == 0)
        {
            plan = null;
            return false;
        }

        var entries = new List<DuckDBStructMutationEntry>(modifications.Count);
        var emittedStructColumns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var modification in modifications)
        {
            var fieldInfo = TryGetStructFieldInfo(modification, columnMap);
            if (fieldInfo is null)
            {
                entries.Add(new DuckDBStructStandaloneEntry(modification));
            }
            else if (emittedStructColumns.Add(fieldInfo.StructColumnName))
            {
                entries.Add(new DuckDBStructGroupEntry(
                    fieldInfo.StructColumnName,
                    groups[fieldInfo.StructColumnName]));
            }
        }

        plan = new DuckDBStructMutationPlan(entries);
        return true;
    }

    private static IReadOnlyDictionary<string, DuckDBStructFieldInfo>? ResolveStructColumnMap(
        IReadOnlyList<IColumnModification> modifications,
        string tableName,
        string? schema)
    {
        var representative = modifications.FirstOrDefault(
            modification => modification.Property?.DeclaringType?.Model is not null);
        if (representative?.Property?.DeclaringType?.Model is not IModel model)
        {
            return null;
        }

        IReadOnlyDictionary<string, DuckDBStructFieldInfo>? firstMatch = null;
        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.GetTableName() != tableName || entityType.GetSchema() != schema)
            {
                continue;
            }

            if (entityType.GetStructColumnMap() is { Count: > 0 } map)
            {
                firstMatch ??= map;
            }
        }

        return firstMatch;
    }

    private static DuckDBStructFieldInfo? TryGetStructFieldInfo(
        IColumnModification modification,
        IReadOnlyDictionary<string, DuckDBStructFieldInfo>? columnMap)
        => columnMap?.TryGetValue(modification.ColumnName, out var fieldInfo) == true
            ? fieldInfo
            : modification.Property?.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value
                as DuckDBStructFieldInfo;
}

internal abstract record DuckDBStructMutationEntry(string ColumnName);

internal sealed record DuckDBStructStandaloneEntry(IColumnModification Modification)
    : DuckDBStructMutationEntry(Modification.ColumnName);

internal sealed record DuckDBStructGroupEntry(
    string StructColumnName,
    IReadOnlyList<(DuckDBStructFieldInfo FieldInfo, IColumnModification Modification)> Fields)
    : DuckDBStructMutationEntry(StructColumnName);
