using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace DuckDB.EFCoreProvider.Storage.Internal;

internal sealed class DuckDBTierMaintenanceBoundary
{
    private readonly ISqlGenerationHelper _sql;
    private readonly DuckDBTierAggregate _aggregate;
    private readonly IReadOnlyList<TierRowIdentity> _rootScope;
    private readonly IReadOnlyDictionary<string, object?> _partitionScope;
    private readonly IReadOnlyDictionary<DuckDBTierNode, IReadOnlyList<TierRowIdentity>> _tombstones;
    private readonly bool _omitRootScope;

    private DuckDBTierMaintenanceBoundary(
        ISqlGenerationHelper sql,
        DuckDBTierAggregate aggregate,
        IReadOnlyList<TierRowIdentity> rootScope,
        IReadOnlyDictionary<string, object?> partitionScope,
        IReadOnlyDictionary<DuckDBTierNode, IReadOnlyList<TierRowIdentity>> tombstones,
        bool omitRootScope)
    {
        _sql = sql;
        _aggregate = aggregate;
        _rootScope = rootScope;
        _partitionScope = partitionScope;
        _tombstones = tombstones;
        _omitRootScope = omitRootScope;
    }

    public bool IsUnbounded
        => _rootScope.Count == 0 && _partitionScope.Count == 0 && _tombstones.Count == 0 && !_omitRootScope;

    public bool HasTombstones => _tombstones.Count > 0 || _omitRootScope;

    public static DuckDBTierMaintenanceBoundary Create(
        ISqlGenerationHelper sql,
        DuckDBTierAggregate aggregate,
        TierMaintenanceScope scope,
        IReadOnlyList<TierRowIdentity> tombstones,
        bool omitRootScope = false)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(tombstones);

        IReadOnlyList<TierRowIdentity> rootScope = [];
        IReadOnlyDictionary<string, object?> partitionScope =
            new Dictionary<string, object?>(StringComparer.Ordinal);
        switch (scope.Kind)
        {
            case TierMaintenanceScopeKind.All:
                break;
            case TierMaintenanceScopeKind.RootMatchKeys:
                ValidateIdentities(aggregate.Root, scope.RootIdentities, "maintenance scope");
                rootScope = scope.RootIdentities;
                break;
            case TierMaintenanceScopeKind.PartitionValues:
                ValidatePartitionScope(aggregate, scope.PartitionValues);
                partitionScope = scope.PartitionValues;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scope), scope.Kind, null);
        }

        var byNode = new Dictionary<DuckDBTierNode, IReadOnlyList<TierRowIdentity>>();
        foreach (var group in tombstones.GroupBy(identity => identity.EntityType))
        {
            var node = aggregate.Nodes.SingleOrDefault(candidate => candidate.Entity.ClrType == group.Key)
                ?? throw new ArgumentException(
                    $"Tombstone entity type '{group.Key.Name}' is not part of aggregate '{aggregate.ControlKey}'.",
                    nameof(tombstones));
            var identities = group.ToArray();
            ValidateIdentities(node, identities, "tombstone");
            byNode[node] = identities;
        }

        if (omitRootScope && scope.Kind == TierMaintenanceScopeKind.All)
        {
            throw new ArgumentException("Omitting cold rows requires an exact root or partition scope.", nameof(scope));
        }

        return new DuckDBTierMaintenanceBoundary(
            sql,
            aggregate,
            rootScope,
            partitionScope,
            byNode,
            omitRootScope);
    }

    public string? RootScopePredicate(string alias)
    {
        if (_rootScope.Count > 0)
        {
            return IdentityPredicate(_aggregate.Root, _rootScope, alias);
        }

        if (_partitionScope.Count == 0)
        {
            return null;
        }

        return string.Join(
            " AND ",
            _aggregate.RootPartitions
                .Take(_partitionScope.Count)
                .Select(partition =>
                {
                    var property = _aggregate.Root.Entity.FindProperty(partition.PropertyName)!;
                    var literal = property.GetRelationalTypeMapping()
                        .GenerateSqlLiteral(_partitionScope[partition.PropertyName]);
                    var source = $"{alias}.{_sql.DelimitIdentifier(partition.SourceColumn)}";
                    var value = partition.Transform switch
                    {
                        TierPartitionTransform.Value => source,
                        TierPartitionTransform.Year => $"CAST(date_trunc('year', {source}) AS DATE)",
                        TierPartitionTransform.Month => $"CAST(date_trunc('month', {source}) AS DATE)",
                        TierPartitionTransform.Day => $"CAST({source} AS DATE)",
                        _ => throw new ArgumentOutOfRangeException(),
                    };
                    return $"{value} = CAST({literal} AS {partition.StoreType})";
                }));
    }

    public string? DirectTombstonePredicate(DuckDBTierNode node, string alias)
        => CombineOr(
            _tombstones.TryGetValue(node, out var identities)
                ? IdentityPredicate(node, identities, alias)
                : null,
            node.IsRoot && _omitRootScope ? RootScopePredicate(alias) : null);

    public string? RootTombstonePredicate(string alias)
        => DirectTombstonePredicate(_aggregate.Root, alias);

    private static string? CombineOr(params string?[] predicates)
    {
        var selected = predicates.Where(predicate => !string.IsNullOrWhiteSpace(predicate)).ToArray();
        return selected.Length == 0
            ? null
            : string.Join(" OR ", selected.Select(predicate => $"({predicate})"));
    }

    private static void ValidateIdentities(
        DuckDBTierNode node,
        IReadOnlyList<TierRowIdentity> identities,
        string purpose)
    {
        var keyProperties = node.Entity.GetTieredStoreMatchProperties();
        if (keyProperties.Count == 0)
        {
            keyProperties = node.Entity.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray();
        }

        var expected = keyProperties.ToHashSet(StringComparer.Ordinal);
        foreach (var identity in identities)
        {
            if (identity.EntityType != node.Entity.ClrType)
            {
                throw new ArgumentException(
                    $"The {purpose} identity type '{identity.EntityType.Name}' does not match '{node.Entity.ClrType.Name}'.");
            }

            if (identity.Values.Count != expected.Count
                || identity.Values.Keys.Any(property => !expected.Contains(property)))
            {
                throw new ArgumentException(
                    $"The {purpose} for '{node.Entity.ClrType.Name}' must provide exactly these configured "
                    + $"match-key properties: {string.Join(", ", keyProperties)}.");
            }

            if (identity.Values.Values.Any(value => value is null))
            {
                throw new ArgumentException(
                    $"The {purpose} for '{node.Entity.ClrType.Name}' contains a null match-key value.");
            }
        }
    }

    private static void ValidatePartitionScope(
        DuckDBTierAggregate aggregate,
        IReadOnlyDictionary<string, object?> values)
    {
        if (values.Count > aggregate.RootPartitions.Count)
        {
            throw new ArgumentException(
                $"Partition scope contains {values.Count} value(s), but aggregate '{aggregate.ControlKey}' declares "
                + $"{aggregate.RootPartitions.Count} partition key(s).");
        }

        var expectedPrefix = aggregate.RootPartitions.Take(values.Count).Select(partition => partition.PropertyName).ToArray();
        if (!values.Keys.SequenceEqual(expectedPrefix, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "Partition scope keys must be supplied in declared leading-prefix order: "
                + string.Join(", ", expectedPrefix) + ".");
        }

        if (values.Values.Any(value => value is null))
        {
            throw new ArgumentException("Partition scope values cannot be null.");
        }
    }

    private string IdentityPredicate(
        DuckDBTierNode node,
        IReadOnlyList<TierRowIdentity> identities,
        string alias)
    {
        var store = StoreObjectIdentifier.Table(node.Table, node.Schema);
        var properties = node.Entity.GetTieredStoreMatchProperties();
        if (properties.Count == 0)
        {
            properties = node.Entity.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray();
        }

        return string.Join(
            " OR ",
            identities.Select(identity =>
                "(" + string.Join(
                    " AND ",
                    properties.Select(propertyName =>
                    {
                        var property = node.Entity.FindProperty(propertyName)!;
                        var column = property.GetColumnName(store)!;
                        var literal = property.GetRelationalTypeMapping()
                            .GenerateSqlLiteral(identity.Values[propertyName]);
                        return $"{alias}.{_sql.DelimitIdentifier(column)} = {literal}";
                    })) + ")"));
    }
}