using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DuckDB.EFCoreProvider.Storage.Internal;

internal sealed record DuckDBTierPartitionPruningPlan(
    IReadOnlyList<DuckDBTierPartitionColumn> Partitions,
    string? ValidationColumn);

internal static class DuckDBTierPartitionPruningMetadata
{
    public static IReadOnlyDictionary<string, DuckDBTierPartitionPruningPlan> ResolveAll(IModel model)
    {
        var candidates = new List<(
            string View,
            IReadOnlyList<DuckDBTierPartitionColumn> Partitions,
            string? ValidationColumn,
            string Source)>();

        candidates.AddRange(
            DuckDBTierAggregate.ResolveAll(model)
                .Where(aggregate => aggregate.Root.ViewName is not null && aggregate.RootPartitions.Count > 0)
                .Select(ResolveOwnerView));

        candidates.AddRange(
            model.GetEntityTypes()
                .Where(entity => entity.GetTieredViewPartitionDefinitions().Count > 0)
                .Select(entity =>
                {
                    var plan = ResolveReadView(entity);
                    return (entity.GetViewName()!, plan.Partitions, plan.ValidationColumn, entity.DisplayName());
                }));

        return candidates
            .GroupBy(candidate => candidate.View, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                    {
                        var definitions = group.First().Partitions;
                        var conflict = group.Skip(1).FirstOrDefault(
                            candidate => !candidate.Partitions.SequenceEqual(definitions));
                        if (conflict != default)
                        {
                            throw new InvalidOperationException(
                                $"Tiered view '{group.Key}' has conflicting partition-pruning metadata between "
                                    + $"'{group.First().Source}' and '{conflict.Source}'.");
                        }

                        var validationColumns = group
                            .Select(candidate => candidate.ValidationColumn)
                            .OfType<string>()
                            .Distinct(StringComparer.Ordinal)
                            .ToArray();
                        if (validationColumns.Length > 1)
                        {
                            throw new InvalidOperationException(
                                $"Tiered view '{group.Key}' has conflicting physical partition contracts.");
                        }

                        return new DuckDBTierPartitionPruningPlan(
                            definitions,
                            validationColumns.SingleOrDefault());
                    },
                StringComparer.Ordinal);
    }

    public static DuckDBTierPartitionPruningPlan ResolveReadView(IReadOnlyEntityType entity)
    {
        var definitions = entity.GetTieredViewPartitionDefinitions();
        if (definitions.Count == 0)
        {
            return new DuckDBTierPartitionPruningPlan([], null);
        }

        var view = entity.GetViewName()
            ?? throw new InvalidOperationException(
                $"Tiered-view reader '{entity.DisplayName()}' must be mapped to a view before pruning metadata is "
                + "configured.");
        if (entity.FindPrimaryKey() is not null)
        {
            throw new InvalidOperationException(
                $"Tiered-view reader '{entity.DisplayName()}' must be keyless because historical rows may reuse "
                + "application keys.");
        }

        var duplicateProperty = definitions
            .GroupBy(definition => definition.PropertyName, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateProperty is not null)
        {
            throw new InvalidOperationException(
                $"Tiered-view reader '{entity.DisplayName()}' declares partition property '{duplicateProperty}' "
                + "more than once.");
        }

        var store = StoreObjectIdentifier.View(view, entity.GetViewSchema());
        var mappedColumns = entity.GetProperties()
            .Select(property => property.GetColumnName(store))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resolved = new List<DuckDBTierPartitionColumn>(definitions.Count);
        foreach (var definition in definitions)
        {
            var property = entity.FindProperty(definition.PropertyName)
                ?? throw new InvalidOperationException(
                    $"Tiered-view partition property '{entity.DisplayName()}.{definition.PropertyName}' is not a "
                    + "mapped scalar property on the reader.");
            var sourceColumn = property.GetColumnName(store)
                ?? throw new InvalidOperationException(
                    $"Tiered-view partition property '{entity.DisplayName()}.{definition.PropertyName}' is not "
                    + $"mapped to view '{view}'.");
            if (definition.Transform != TierPartitionTransform.Value && !IsDateProperty(property.ClrType))
            {
                throw new InvalidOperationException(
                    $"Tiered-view partition '{entity.DisplayName()}.{definition.PropertyName}' uses "
                    + $"{definition.Transform} bucketing, but its CLR type "
                    + $"'{property.ClrType.ShortDisplayName()}' is not DateTime or DateOnly.");
            }

            var partitionColumn = definition.ResolveName(sourceColumn);
            if ((definition.Transform != TierPartitionTransform.Value
                    || !string.Equals(partitionColumn, sourceColumn, StringComparison.OrdinalIgnoreCase))
                && mappedColumns.Contains(partitionColumn))
            {
                throw new InvalidOperationException(
                    $"Tiered-view partition '{entity.DisplayName()}.{definition.PropertyName}' uses Hive name "
                    + $"'{partitionColumn}', which collides with a mapped view column.");
            }

            if (resolved.Any(
                    partition => string.Equals(
                        partition.Name,
                        partitionColumn,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Tiered-view reader '{entity.DisplayName()}' maps more than one partition property to column "
                    + $"'{partitionColumn}'.");
            }

            resolved.Add(new DuckDBTierPartitionColumn(
                definition.PropertyName,
                sourceColumn,
                partitionColumn,
                definition.Transform == TierPartitionTransform.Value
                    ? property.GetColumnType(store) ?? property.GetRelationalTypeMapping().StoreType
                    : "DATE",
                definition.Transform));
        }

        var validationColumn = DuckDBTierPartitionContract.GetValidationColumn(resolved);
        if (mappedColumns.Contains(validationColumn))
        {
            throw new InvalidOperationException(
                $"Tiered-view reader '{entity.DisplayName()}' maps provider-owned partition contract column "
                + $"'{validationColumn}'. Choose a different source column mapping.");
        }

        return new DuckDBTierPartitionPruningPlan(resolved, validationColumn);
    }

    private static (
        string View,
        IReadOnlyList<DuckDBTierPartitionColumn> Partitions,
        string? ValidationColumn,
        string Source) ResolveOwnerView(DuckDBTierAggregate aggregate)
    {
        var validationColumn = DuckDBTierPartitionContract.GetValidationColumn(aggregate.RootPartitions);
        if (aggregate.Root.Columns.Contains(validationColumn, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Tiered-storage root '{aggregate.Root.Entity.DisplayName()}' maps provider-owned partition contract "
                + $"column '{validationColumn}'. Choose a different source column mapping.");
        }

        return (
            aggregate.Root.ViewName!,
            aggregate.RootPartitions,
            null,
            aggregate.Root.Entity.DisplayName());
    }

    private static bool IsDateProperty(Type clrType)
    {
        clrType = Nullable.GetUnderlyingType(clrType) ?? clrType;
        return clrType == typeof(DateTime) || clrType == typeof(DateOnly);
    }
}