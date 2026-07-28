using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Collections.Immutable;

namespace DuckDB.EFCoreProvider.Migrations;

/// <summary>
///     Resolves flattened relational columns into immutable physical DuckDB table plans.
/// </summary>
internal static class DuckDBStructSchemaPlanner
{
    public static DuckDBCreateTableStructPlan PlanCreateTable(CreateTableOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var fields = operation.Columns
            .Select((column, ordinal) => (
                Column: column,
                Ordinal: ordinal,
                FieldInfo: column.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value
                    as DuckDBStructFieldInfo))
            .Where(field => field.FieldInfo is not null)
            .Select(field => new PlannedField(
                field.Ordinal,
                field.Column.Name,
                RequireStoreType(field.Column),
                field.Column.IsNullable,
                field.Column,
                field.FieldInfo))
            .ToArray();
        ValidateFields(fields);

        var replacements = fields
            .GroupBy(field => field.FieldInfo!.StructColumnName, StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateStructColumn(group.Key, group))
            .ToImmutableDictionary(column => column.SourceOrdinal);
        var suppressed = fields
            .Select(field => field.Ordinal)
            .Where(ordinal => !replacements.ContainsKey(ordinal))
            .ToImmutableHashSet();
        return new DuckDBCreateTableStructPlan(replacements, suppressed);
    }

    public static DuckDBTableRebuildPlan PlanTable(ITable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var orderedColumns = table.Columns
            .Where(column => column.Order.HasValue)
            .OrderBy(column => column.Order)
            .Concat(table.Columns.Where(column => !column.Order.HasValue))
            .ToArray();
        var fields = orderedColumns
            .Select((column, ordinal) => new PlannedField(
                ordinal,
                column.Name,
                column.StoreType,
                column.IsNullable,
                column,
                column.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value
                    as DuckDBStructFieldInfo))
            .Where(field => field.FieldInfo is not null)
            .ToArray();
        ValidateFields(fields);

        var structColumns = fields
            .GroupBy(field => field.FieldInfo!.StructColumnName, StringComparer.OrdinalIgnoreCase)
            .Select(group => CreateStructColumn(group.Key, group))
            .ToDictionary(column => column.SourceOrdinal);
        var suppressed = fields
            .Select(field => field.Ordinal)
            .Where(ordinal => !structColumns.ContainsKey(ordinal))
            .ToHashSet();

        var columns = new List<DuckDBPhysicalColumnPlan>(orderedColumns.Length);
        for (var ordinal = 0; ordinal < orderedColumns.Length; ordinal++)
        {
            if (structColumns.TryGetValue(ordinal, out var structColumn))
            {
                columns.Add(structColumn);
            }
            else if (!suppressed.Contains(ordinal))
            {
                columns.Add(CreateScalarColumn(orderedColumns[ordinal], ordinal));
            }
        }

        return new DuckDBTableRebuildPlan(
            table.Name,
            table.Schema,
            table.Comment,
            columns,
            columns
                .Where(column => column.ComputedColumnSql is null)
                .Select(column => column.Name));
    }

    public static void ValidateStandaloneColumnOperation(ColumnOperation operation, string operationName)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value
            is DuckDBStructFieldInfo field)
        {
            throw new NotSupportedException(
                $"Cannot {operationName} column '{operation.Name}' on table '{operation.Table}' because it maps to "
                + $"DuckDB STRUCT path '{FormatPath(field)}'. Change the complete STRUCT mapping through a table rebuild.");
        }
    }

    public static void ValidateAnnotatedOperation(
        MigrationOperation operation,
        string columnName,
        string tableName,
        string operationName)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value
            is DuckDBStructFieldInfo field)
        {
            throw new NotSupportedException(
                $"Cannot {operationName} column '{columnName}' on table '{tableName}' because it maps to "
                + $"DuckDB STRUCT path '{FormatPath(field)}'. Change the complete STRUCT mapping through a table rebuild.");
        }
    }

    private static DuckDBStructColumnPlan CreateStructColumn(
        string structColumnName,
        IEnumerable<PlannedField> fields)
    {
        var fieldArray = fields.ToArray();
        var root = new MutableFieldNode(null);
        foreach (var field in fieldArray)
        {
            var current = root;
            foreach (var nestedName in field.FieldInfo!.NestedFieldNames)
            {
                current = current.GetOrAdd(nestedName);
            }

            current.AddLeaf(
                field.FieldInfo.LeafFieldName ?? field.Name,
                field.StoreType,
                field.IsNullable);
        }

        // Optional STRUCT roots are rejected during model validation, so physical roots are required.
        return new DuckDBStructColumnPlan(
            fieldArray.Min(field => field.Ordinal),
            structColumnName,
            isNullable: false,
            root.Freeze());
    }

    private static DuckDBScalarColumnPlan CreateScalarColumn(IColumn column, int ordinal)
    {
        column.TryGetDefaultValue(out var defaultValue);
        return new DuckDBScalarColumnPlan(
            ordinal,
            column.Name,
            column.StoreTypeMapping.ClrType,
            column.StoreType,
            column.IsNullable,
            defaultValue,
            column.DefaultValueSql,
            column.ComputedColumnSql,
            column.IsStored,
            column.Comment,
            column.Collation,
            column.GetAnnotations().Select(annotation =>
                new KeyValuePair<string, object?>(annotation.Name, annotation.Value)));
    }

    private static void ValidateFields(IReadOnlyList<PlannedField> fields)
    {
        foreach (var field in fields)
        {
            if (field.Source is ColumnOperation operation
                && (operation.DefaultValue is not null
                    || operation.DefaultValueSql is not null
                    || operation.ComputedColumnSql is not null
                    || operation.Comment is not null))
            {
                throw new NotSupportedException(
                    $"DuckDB STRUCT path '{FormatPath(field.FieldInfo!)}' does not support defaults, computed values, "
                    + "or column comments.");
            }
        }

        var duplicate = fields
            .GroupBy(
                field => field.FieldInfo!.StructColumnName
                    + "\0"
                    + string.Join("\0", field.FieldInfo.FieldPath),
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Multiple relational columns map to DuckDB STRUCT path '{FormatPath(duplicate.First().FieldInfo!)}'.");
        }

        var collision = DuckDBStructPathCollision.Find(fields.Select(field => field.FieldInfo!));
        if (collision is { } conflict)
        {
            var (root, leafPath, nestedPath) = conflict;
            throw new InvalidOperationException(
                $"DuckDB STRUCT root '{root}' has conflicting paths: "
                + $"'{DuckDBStructPathCollision.FormatPath(root, leafPath)}' is used as a scalar leaf "
                + $"and as a parent of '{DuckDBStructPathCollision.FormatPath(root, nestedPath)}'.");
        }
    }

    private static string RequireStoreType(AddColumnOperation column)
        => column.ColumnType
            ?? throw new InvalidOperationException(
                $"Column '{column.Name}' has no store type while planning a DuckDB STRUCT.");

    private static string FormatPath(DuckDBStructFieldInfo field)
        => string.Join(".", new[] { field.StructColumnName }.Concat(field.FieldPath));

    private sealed record PlannedField(
        int Ordinal,
        string Name,
        string StoreType,
        bool IsNullable,
        object Source,
        DuckDBStructFieldInfo? FieldInfo);

    private sealed class MutableFieldNode(string? fieldName)
    {
        private readonly List<MutableFieldNode> _children = [];

        public string? FieldName { get; } = fieldName;

        public string? StoreType { get; private init; }

        public bool IsNullable { get; private init; }

        public MutableFieldNode GetOrAdd(string name)
        {
            var child = _children.FirstOrDefault(
                candidate => string.Equals(candidate.FieldName, name, StringComparison.OrdinalIgnoreCase));
            if (child is null)
            {
                child = new MutableFieldNode(name);
                _children.Add(child);
            }
            else if (child.StoreType is not null)
            {
                throw new InvalidOperationException(
                    $"DuckDB STRUCT field '{name}' is mapped as both a scalar and nested structure.");
            }

            return child;
        }

        public void AddLeaf(string name, string storeType, bool isNullable)
        {
            if (_children.Any(candidate => string.Equals(
                    candidate.FieldName,
                    name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Duplicate DuckDB STRUCT field '{name}'.");
            }

            _children.Add(new MutableFieldNode(name)
            {
                StoreType = storeType,
                IsNullable = isNullable
            });
        }

        public DuckDBStructSchemaFieldPlan Freeze()
            => new(
                FieldName,
                StoreType,
                IsNullable,
                _children.Select(child => child.Freeze()));
    }
}

internal sealed class DuckDBCreateTableStructPlan
{
    private readonly ImmutableDictionary<int, DuckDBStructColumnPlan> _replacements;
    private readonly ImmutableHashSet<int> _suppressedOrdinals;

    public DuckDBCreateTableStructPlan(
        ImmutableDictionary<int, DuckDBStructColumnPlan> replacements,
        ImmutableHashSet<int> suppressedOrdinals)
    {
        _replacements = replacements;
        _suppressedOrdinals = suppressedOrdinals;
    }

    public bool HasStructColumns => _replacements.Count > 0;

    public bool TryGetReplacement(int ordinal, out DuckDBStructColumnPlan plan)
        => _replacements.TryGetValue(ordinal, out plan!);

    public bool IsSuppressed(int ordinal)
        => _suppressedOrdinals.Contains(ordinal);
}

internal sealed class DuckDBTableRebuildPlan
{
    public DuckDBTableRebuildPlan(
        string tableName,
        string? schema,
        string? comment,
        IEnumerable<DuckDBPhysicalColumnPlan> columns,
        IEnumerable<string> copyColumnNames)
    {
        TableName = tableName;
        Schema = schema;
        Comment = comment;
        Columns = columns.ToImmutableArray();
        CopyColumnNames = copyColumnNames.ToImmutableArray();
    }

    public string TableName { get; }

    public string? Schema { get; }

    public string? Comment { get; }

    public IReadOnlyList<DuckDBPhysicalColumnPlan> Columns { get; }

    public IReadOnlyList<string> CopyColumnNames { get; }
}

internal abstract class DuckDBPhysicalColumnPlan
{
    protected DuckDBPhysicalColumnPlan(
        int sourceOrdinal,
        string name,
        Type clrType,
        string storeType,
        bool isNullable,
        string? computedColumnSql)
    {
        SourceOrdinal = sourceOrdinal;
        Name = name;
        ClrType = clrType;
        StoreType = storeType;
        IsNullable = isNullable;
        ComputedColumnSql = computedColumnSql;
    }

    public int SourceOrdinal { get; }

    public string Name { get; }

    public Type ClrType { get; }

    public string StoreType { get; }

    public bool IsNullable { get; }

    public string? ComputedColumnSql { get; }
}

internal sealed class DuckDBStructColumnPlan : DuckDBPhysicalColumnPlan
{
    public DuckDBStructColumnPlan(
        int sourceOrdinal,
        string name,
        bool isNullable,
        DuckDBStructSchemaFieldPlan root)
        : base(sourceOrdinal, name, typeof(object), string.Empty, isNullable, null)
        => Root = root;

    public DuckDBStructSchemaFieldPlan Root { get; }
}

internal sealed class DuckDBScalarColumnPlan : DuckDBPhysicalColumnPlan
{
    public DuckDBScalarColumnPlan(
        int sourceOrdinal,
        string name,
        Type clrType,
        string storeType,
        bool isNullable,
        object? defaultValue,
        string? defaultValueSql,
        string? computedColumnSql,
        bool? isStored,
        string? comment,
        string? collation,
        IEnumerable<KeyValuePair<string, object?>> annotations)
        : base(sourceOrdinal, name, clrType, storeType, isNullable, computedColumnSql)
    {
        DefaultValue = defaultValue;
        DefaultValueSql = defaultValueSql;
        IsStored = isStored;
        Comment = comment;
        Collation = collation;
        Annotations = annotations.ToImmutableArray();
    }

    public object? DefaultValue { get; }

    public string? DefaultValueSql { get; }

    public bool? IsStored { get; }

    public string? Comment { get; }

    public string? Collation { get; }

    public IReadOnlyList<KeyValuePair<string, object?>> Annotations { get; }
}

internal sealed class DuckDBStructSchemaFieldPlan
{
    public DuckDBStructSchemaFieldPlan(
        string? fieldName,
        string? storeType,
        bool isNullable,
        IEnumerable<DuckDBStructSchemaFieldPlan> children)
    {
        FieldName = fieldName;
        StoreType = storeType;
        IsNullable = isNullable;
        Children = children.ToImmutableArray();
    }

    public string? FieldName { get; }

    public string? StoreType { get; }

    public bool IsNullable { get; }

    public IReadOnlyList<DuckDBStructSchemaFieldPlan> Children { get; }

    public bool IsLeaf => StoreType is not null;
}