using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Text;

namespace DuckDB.EFCoreProvider.Migrations;

/// <summary>
///     Owns STRUCT DDL consolidation so EnsureCreated and table rebuilds use the same schema shape.
/// </summary>
internal static class DuckDBStructSchemaPlanner
{
    public static CreateTableOperation ConsolidateCreateTable(CreateTableOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var structGroups = new Dictionary<string, List<(AddColumnOperation Column, DuckDBStructFieldInfo FieldInfo)>>();
        var structColumns = new HashSet<AddColumnOperation>();
        foreach (var column in operation.Columns)
        {
            if (column.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value is DuckDBStructFieldInfo fieldInfo)
            {
                structGroups.TryAdd(fieldInfo.StructColumnName, []);
                structGroups[fieldInfo.StructColumnName].Add((column, fieldInfo));
                structColumns.Add(column);
            }
        }

        if (structGroups.Count == 0)
        {
            return operation;
        }

        var consolidated = new CreateTableOperation
        {
            Name = operation.Name,
            Schema = operation.Schema,
            Comment = operation.Comment,
            PrimaryKey = operation.PrimaryKey
        };
        consolidated.AddAnnotations(operation.GetAnnotations());

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in operation.Columns)
        {
            if (!structColumns.Contains(column))
            {
                consolidated.Columns.Add(column);
                continue;
            }

            var fieldInfo = (DuckDBStructFieldInfo)column.FindAnnotation(DuckDBAnnotationNames.StructField)!.Value!;
            if (!emitted.Add(fieldInfo.StructColumnName))
            {
                continue;
            }

            var fields = structGroups[fieldInfo.StructColumnName];
            consolidated.Columns.Add(new AddColumnOperation
            {
                Name = fieldInfo.StructColumnName,
                Table = operation.Name,
                Schema = operation.Schema,
                ClrType = typeof(object),
                ColumnType = BuildStructStoreType(
                    fields.Select(field => (field.Column.Name, field.Column.ColumnType!, field.FieldInfo)).ToArray()),
                IsNullable = fields.Any(field => field.Column.IsNullable)
            });
        }

        consolidated.ForeignKeys.AddRange(operation.ForeignKeys);
        consolidated.UniqueConstraints.AddRange(operation.UniqueConstraints);
        consolidated.CheckConstraints.AddRange(operation.CheckConstraints);
        return consolidated;
    }

    public static string BuildStructStoreType(
        IReadOnlyList<(string Name, string StoreType, DuckDBStructFieldInfo FieldInfo)> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var root = new StructFieldNode();
        foreach (var (name, storeType, fieldInfo) in fields)
        {
            var current = root;
            foreach (var nestedName in fieldInfo.NestedFieldNames)
            {
                var child = current.Children.FirstOrDefault(candidate => candidate.FieldName == nestedName);
                if (child is null)
                {
                    child = new StructFieldNode { FieldName = nestedName };
                    current.Children.Add(child);
                }

                current = child;
            }

            current.Children.Add(new StructFieldNode
            {
                FieldName = fieldInfo.LeafFieldName ?? name,
                StoreType = storeType
            });
        }

        return RenderStructType(root);
    }

    private static string RenderStructType(StructFieldNode node)
    {
        var builder = new StringBuilder("STRUCT(");
        for (var i = 0; i < node.Children.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            var child = node.Children[i];
            builder.Append('"')
                .Append(child.FieldName!.Replace("\"", "\"\"", StringComparison.Ordinal))
                .Append("\" ");
            builder.Append(child.StoreType ?? RenderStructType(child));
        }

        return builder.Append(')').ToString();
    }

    private sealed class StructFieldNode
    {
        public string? FieldName { get; init; }
        public string? StoreType { get; init; }
        public List<StructFieldNode> Children { get; } = [];
    }
}
