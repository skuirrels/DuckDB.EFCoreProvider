using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     Resolves flattened EF STRUCT leaf columns into typed provider expressions before SQL
///     generation. Only columns from direct physical tables in the current SELECT scope are
///     rewritten; outer references to subquery projections remain ordinary columns.
/// </summary>
internal sealed class DuckDBStructFieldRewritingExpressionVisitor : ExpressionVisitor
{
    private IReadOnlyDictionary<string, DuckDBStructEntityMetadata> _directTables =
        new Dictionary<string, DuckDBStructEntityMetadata>(StringComparer.Ordinal);
    private IReadOnlySet<string> _directTableAliases = new HashSet<string>(StringComparer.Ordinal);

    protected override Expression VisitExtension(Expression extensionExpression)
    {
        if (extensionExpression is ColumnExpression columnExpression)
        {
            return RewriteColumn(columnExpression);
        }

        if (extensionExpression is ShapedQueryExpression shapedQueryExpression)
        {
            return shapedQueryExpression.Update(
                Visit(shapedQueryExpression.QueryExpression),
                Visit(shapedQueryExpression.ShaperExpression));
        }

        if (extensionExpression is not SelectExpression selectExpression)
        {
            return base.VisitExtension(extensionExpression);
        }

        var previousTables = _directTables;
        var previousAliases = _directTableAliases;
        (_directTables, _directTableAliases) = CollectDirectStructTables(selectExpression.Tables);
        try
        {
            return base.VisitExtension(selectExpression);
        }
        finally
        {
            _directTables = previousTables;
            _directTableAliases = previousAliases;
        }
    }

    private Expression RewriteColumn(ColumnExpression columnExpression)
    {
        DuckDBStructFieldInfo? field = null;
        if (_directTables.TryGetValue(columnExpression.TableAlias, out var metadata))
        {
            metadata.TryGetField(columnExpression.Name, out field!);
            if (field is null)
            {
                field = metadata.Columns
                    .FirstOrDefault(pair => string.Equals(
                        pair.Key,
                        columnExpression.Name,
                        StringComparison.OrdinalIgnoreCase))
                    .Value;
            }
        }
        if (field is null && _directTableAliases.Contains(columnExpression.TableAlias))
        {
            field = columnExpression.Column?.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value
                as DuckDBStructFieldInfo;
            if (field is null && columnExpression.Column is { PropertyMappings.Count: > 0 } column)
            {
                field = column.PropertyMappings
                    .Select(mapping => mapping.Property.GetStructFieldInfo())
                    .FirstOrDefault(candidate => candidate is not null);
            }
        }

        if (field is null)
        {
            return base.VisitExtension(columnExpression);
        }

        var fieldPath = field.FieldPath;
        if (field.LeafFieldName is null)
        {
            fieldPath = [..field.NestedFieldNames, columnExpression.Name];
        }

        var source = new ColumnExpression(
            field.StructColumnName,
            columnExpression.TableAlias,
            typeof(object),
            typeMapping: null,
            nullable: columnExpression.IsNullable);
        return new DuckDBStructFieldExpression(
            source,
            fieldPath,
            columnExpression.Type,
            columnExpression.TypeMapping);
    }

    private static (
        IReadOnlyDictionary<string, DuckDBStructEntityMetadata> Metadata,
        IReadOnlySet<string> Aliases) CollectDirectStructTables(
        IReadOnlyList<TableExpressionBase> tables)
    {
        var result = new Dictionary<string, DuckDBStructEntityMetadata>(StringComparer.Ordinal);
        var aliases = new HashSet<string>(StringComparer.Ordinal);

        foreach (var table in tables)
        {
            if (table is JoinExpressionBase join)
            {
                AddTables([join.Table], result, aliases);
            }
            else
            {
                AddTables([table], result, aliases);
            }
        }

        return (result, aliases);
    }

    private static void AddTables(
        IEnumerable<TableExpressionBase> tables,
        Dictionary<string, DuckDBStructEntityMetadata> target,
        HashSet<string> aliases)
    {
        foreach (var table in tables)
        {
            if (table is JoinExpressionBase join)
            {
                AddTables([join.Table], target, aliases);
                continue;
            }

            if (table is not ITableBasedExpression { Table: not null } tableBased
                || table.Alias is null)
            {
                continue;
            }

            aliases.Add(table.Alias);
            var metadataByEntity = tableBased.Table.EntityTypeMappings
                .Select(mapping => mapping.TypeBase)
                .OfType<IEntityType>()
                .Select(entityType => entityType.GetStructMetadata())
                .Where(metadata => metadata is not null)
                .Cast<DuckDBStructEntityMetadata>()
                .ToArray();
            if (metadataByEntity.Length > 0)
            {
                target[table.Alias] = MergeMetadata(metadataByEntity);
            }
        }
    }

    private static DuckDBStructEntityMetadata MergeMetadata(
        IReadOnlyList<DuckDBStructEntityMetadata> metadataByEntity)
    {
        var columns = metadataByEntity
            .SelectMany(metadata => metadata.Columns)
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                if (group.Skip(1).Any(pair => !HasSameMapping(pair.Value, first.Value)))
                {
                    throw new InvalidOperationException(
                        $"Conflicting STRUCT mappings were found for EF column '{group.Key}'.");
                }

                return first;
            });

        return new DuckDBStructEntityMetadata(
            metadataByEntity.SelectMany(metadata => metadata.Roots),
            columns);
    }

    private static bool HasSameMapping(DuckDBStructFieldInfo left, DuckDBStructFieldInfo right)
        => string.Equals(left.StructColumnName, right.StructColumnName, StringComparison.Ordinal)
            && left.FieldPath.SequenceEqual(right.FieldPath, StringComparer.Ordinal)
            && string.Equals(left.EfColumnName, right.EfColumnName, StringComparison.Ordinal)
            && string.Equals(left.StoreType, right.StoreType, StringComparison.Ordinal)
            && left.IsNullable == right.IsNullable;
}
