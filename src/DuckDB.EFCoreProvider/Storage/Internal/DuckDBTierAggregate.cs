using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Text.Json;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to the
///     same compatibility standards as public APIs. It may be changed or removed without notice in any release.
///     <para>
///         Resolves a tiered-storage aggregate (root + children) from the model: each node's table, columns,
///         view name, per-table archive subdirectory, and the foreign-key chain up to the root used for the
///         archive join and the child hot/cold boundary. Nodes are ordered with parents before children.
///     </para>
/// </summary>
public sealed class DuckDBTierAggregate
{
    private DuckDBTierAggregate(
        IReadOnlyList<DuckDBTierNode> nodes,
        string archiveBasePath,
        string controlKey,
        TierGranularity granularity,
        string rootTimestampColumn,
        IReadOnlyList<DuckDBTierPartitionColumn> rootPartitions,
        bool includeHotChildFilter)
    {
        Nodes = nodes;
        ArchiveBasePath = archiveBasePath.TrimEnd('/', '\\');
        ControlKey = controlKey;
        Granularity = granularity;
        RootTimestampColumn = rootTimestampColumn;
        RootPartitions = rootPartitions;
        RootPartitionColumns = rootPartitions.Select(partition => partition.Name).ToArray();
        PartitionSpec = JsonSerializer.Serialize(new DuckDBTierPartitionLayout(2, granularity, rootPartitions));
        ArchiveSpec = JsonSerializer.Serialize(new DuckDBTierArchiveContract(
            1,
            controlKey,
            nodes[0].ArchiveSubPath,
            granularity,
            rootTimestampColumn,
            PartitionSpec,
            nodes.Select(node => new DuckDBTierArchiveNodeContract(
                node.Table,
                node.Schema,
                node.ColumnDefinitions.OrderBy(column => column.Name, StringComparer.Ordinal).ToArray(),
                node.KeyColumns.ToArray(),
                node.ComparisonColumns.ToArray())).ToArray()));
        IncludeHotChildFilter = includeHotChildFilter;
    }

    /// <summary>The aggregate nodes, root first, every parent before its children.</summary>
    public IReadOnlyList<DuckDBTierNode> Nodes { get; }

    /// <summary>The tier control-table key (shared by the whole aggregate).</summary>
    public string ControlKey { get; }

    /// <summary>The configured aggregate archive base beneath which table directories are stored.</summary>
    public string ArchiveBasePath { get; }

    /// <summary>The archive partition granularity.</summary>
    public TierGranularity Granularity { get; }

    /// <summary>The physical timestamp column on the root table.</summary>
    public string RootTimestampColumn { get; }

    /// <summary>The physical root columns used as additional Hive partition keys, in declaration order.</summary>
    public IReadOnlyList<string> RootPartitionColumns { get; }

    /// <summary>The physical names and DuckDB store types of the additional root partition keys.</summary>
    public IReadOnlyList<DuckDBTierPartitionColumn> RootPartitions { get; }

    /// <summary>The persisted layout signature used to reject incompatible archive configuration changes.</summary>
    public string PartitionSpec { get; }

    /// <summary>The persisted aggregate contract used to reject unsafe path, key, and schema changes.</summary>
    public string ArchiveSpec { get; }

    /// <summary>Whether child views include the "root is hot" semijoin guard.</summary>
    public bool IncludeHotChildFilter { get; }

    /// <summary>The root node.</summary>
    public DuckDBTierNode Root => Nodes[0];

    /// <summary>Resolves the aggregate rooted at the tiered entity with the given CLR type, or <see langword="null" />.</summary>
    public static DuckDBTierAggregate? Resolve(IModel model, Type rootClrType)
    {
        var root = model.GetEntityTypes().FirstOrDefault(
            e => e.GetTieredStoreRole() == "Root" && e.ClrType == rootClrType);
        return root is null ? null : Build(model, root);
    }

    /// <summary>Resolves every tiered aggregate configured in the model.</summary>
    public static IReadOnlyList<DuckDBTierAggregate> ResolveAll(IModel model)
        => model.GetEntityTypes()
            .Where(e => e.GetTieredStoreRole() == "Root")
            .Select(root => Build(model, root))
            .ToList();

    private static DuckDBTierAggregate Build(IModel model, IEntityType root)
    {
        var archivePath = root.GetTieredStoreArchivePath()!;
        var rootStore = StoreObjectIdentifier.Table(root.GetTableName()!, root.GetSchema());
        var rootTimestampColumn = root.FindProperty(root.GetTieredStoreTimestamp()!)!.GetColumnName(rootStore)!;
        var rootPartitions = root.GetTieredStorePartitionDefinitions()
            .Select(definition =>
            {
                var property = root.FindProperty(definition.PropertyName)!;
                var sourceColumn = property.GetColumnName(rootStore)!;
                return new DuckDBTierPartitionColumn(
                    definition.PropertyName,
                    sourceColumn,
                    definition.Transform == TierPartitionTransform.Value
                        ? sourceColumn
                        : sourceColumn + "_" + definition.Transform.ToString().ToLowerInvariant(),
                    definition.Transform == TierPartitionTransform.Value
                        ? property.GetColumnType(rootStore) ?? property.GetRelationalTypeMapping().StoreType
                        : "DATE",
                    definition.Transform,
                    definition.IsImplicit);
            })
            .ToList();

        var nodes = new List<DuckDBTierNode> { BuildNode(root, archivePath, []) };

        var children = model.GetEntityTypes()
            .Where(e => e.GetTieredStoreRole() == "Child" && e.GetTieredStoreRoot() == root.Name)
            .Select(child => (Entity: child, Chain: BuildChain(model, child, root)))
            .OrderBy(x => x.Chain.Count)
            .ThenBy(x => x.Entity.Name, StringComparer.Ordinal); // parents before children, stable within a depth

        foreach (var (entity, chain) in children)
        {
            nodes.Add(BuildNode(entity, archivePath, chain));
        }

        return new DuckDBTierAggregate(
            nodes, archivePath, root.GetTieredStoreControlKey()!, root.GetTieredStoreGranularity(), rootTimestampColumn,
            rootPartitions, root.GetTieredStoreHotChildFilter());
    }

    private static DuckDBTierNode BuildNode(IEntityType entity, string archivePath, IReadOnlyList<DuckDBTierControl.TierJoinHop> chain)
    {
        var table = entity.GetTableName()!;
        var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
        var columnDefinitions = entity.GetProperties()
            .Select(property => (Property: property, Column: property.GetColumnName(store)))
            .Where(mapped => mapped.Column is not null)
            .Select(mapped => new DuckDBTierColumn(
                mapped.Property.Name,
                mapped.Column!,
                mapped.Property.GetColumnType(store) ?? mapped.Property.GetRelationalTypeMapping().StoreType,
                mapped.Property.IsNullable))
            .ToList();
        var configuredMatchKey = entity.GetTieredStoreMatchProperties();
        var keyProperties = configuredMatchKey.Count == 0
            ? entity.FindPrimaryKey()?.Properties ?? []
            : configuredMatchKey.Select(name => entity.FindProperty(name)!).ToArray();
        var keyColumns = keyProperties
            .Select(p => p.GetColumnName(store))
            .OfType<string>()
            .ToList();
        var comparisonColumns = columnDefinitions.Select(column => column.Name).ToList();
        if (configuredMatchKey.Count > 0)
        {
            var localIdentityColumns = entity.FindPrimaryKey()!.Properties
                .Select(property => property.GetColumnName(store))
                .OfType<string>()
                .Where(column => !keyColumns.Contains(column, StringComparer.Ordinal));
            comparisonColumns.RemoveAll(column => localIdentityColumns.Contains(column, StringComparer.Ordinal));

            // A replayed child graph can receive a new provider-local parent key even though both nodes retain
            // their stable external identities. Do not turn that relationship plumbing into a false correction.
            if (chain.Count > 0 && !keyColumns.Contains(chain[0].ForeignKeyColumn, StringComparer.Ordinal))
            {
                comparisonColumns.Remove(chain[0].ForeignKeyColumn);
            }
        }

        return new DuckDBTierNode(
            entity, table, entity.GetSchema(), columnDefinitions.Select(column => column.Name).ToArray(),
            columnDefinitions, keyColumns, comparisonColumns, entity.GetTieredStoreView(),
            archivePath.TrimEnd('/', '\\') + "/" + table, chain);
    }

    private static List<DuckDBTierControl.TierJoinHop> BuildChain(IModel model, IEntityType child, IEntityType root)
    {
        var chain = new List<DuckDBTierControl.TierJoinHop>();
        var current = child;

        while (current.Name != root.Name)
        {
            var parent = model.FindEntityType(current.GetTieredStoreParent()!)!;
            var navigation = parent.FindNavigation(current.GetTieredStoreParentNavigation()!)!;
            var foreignKey = navigation.ForeignKey;

            var currentStore = StoreObjectIdentifier.Table(current.GetTableName()!, current.GetSchema());
            var parentStore = StoreObjectIdentifier.Table(parent.GetTableName()!, parent.GetSchema());

            chain.Add(new DuckDBTierControl.TierJoinHop(
                foreignKey.Properties[0].GetColumnName(currentStore)!,
                parent.GetTableName()!,
                parent.GetSchema(),
                foreignKey.PrincipalKey.Properties[0].GetColumnName(parentStore)!));

            current = parent;
        }

        return chain;
    }
}

/// <summary>A resolved root-owned Hive partition key and its physical transformation. Internal API.</summary>
public sealed record DuckDBTierPartitionColumn(
    string PropertyName,
    string SourceColumn,
    string Name,
    string StoreType,
    TierPartitionTransform Transform,
    bool IsImplicit = false)
{
    /// <summary>Creates an exact-value partition used by compatibility SQL-building overloads.</summary>
    public DuckDBTierPartitionColumn(string name, string storeType)
        : this(name, name, name, storeType, TierPartitionTransform.Value)
    {
    }
}

/// <summary>The versioned physical partition layout persisted in the tier control table. Internal API.</summary>
public sealed record DuckDBTierPartitionLayout(
    int Version,
    TierGranularity Granularity,
    IReadOnlyList<DuckDBTierPartitionColumn> Columns);

/// <summary>A mapped column recorded in the tiered archive contract. Internal API.</summary>
public sealed record DuckDBTierColumn(string PropertyName, string Name, string StoreType, bool IsNullable);

/// <summary>The versioned aggregate-wide archive contract. Internal API.</summary>
public sealed record DuckDBTierArchiveContract(
    int Version,
    string ControlKey,
    string ArchivePath,
    TierGranularity Granularity,
    string LifecycleColumn,
    string PartitionSpec,
    IReadOnlyList<DuckDBTierArchiveNodeContract> Nodes);

/// <summary>A table-level entry in the versioned tiered archive contract. Internal API.</summary>
public sealed record DuckDBTierArchiveNodeContract(
    string Table,
    string? Schema,
    IReadOnlyList<DuckDBTierColumn> Columns,
    IReadOnlyList<string> MatchKeyColumns,
    IReadOnlyList<string> ComparisonColumns);

/// <summary>One resolved node (table) of a tiered aggregate. Internal API.</summary>
public sealed record DuckDBTierNode(
    IEntityType Entity,
    string Table,
    string? Schema,
    IReadOnlyList<string> Columns,
    IReadOnlyList<DuckDBTierColumn> ColumnDefinitions,
    IReadOnlyList<string> KeyColumns,
    IReadOnlyList<string> ComparisonColumns,
    string? ViewName,
    string ArchiveSubPath,
    IReadOnlyList<DuckDBTierControl.TierJoinHop> ChainToRoot)
{
    /// <summary>Creates a node using the pre-1.6 internal constructor shape.</summary>
    public DuckDBTierNode(
        IEntityType entity,
        string table,
        string? schema,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> keyColumns,
        string? viewName,
        string archiveSubPath,
        IReadOnlyList<DuckDBTierControl.TierJoinHop> chainToRoot)
        : this(
            entity,
            table,
            schema,
            columns,
            CreateColumnDefinitions(entity, table, schema, columns),
            keyColumns,
            columns,
            viewName,
            archiveSubPath,
            chainToRoot)
    {
    }

    /// <summary><see langword="true" /> for the aggregate root (no chain to a parent).</summary>
    public bool IsRoot => ChainToRoot.Count == 0;

    /// <summary>Deconstructs a node using the pre-1.6 internal member shape.</summary>
    public void Deconstruct(
        out IEntityType entity,
        out string table,
        out string? schema,
        out IReadOnlyList<string> columns,
        out IReadOnlyList<string> keyColumns,
        out string? viewName,
        out string archiveSubPath,
        out IReadOnlyList<DuckDBTierControl.TierJoinHop> chainToRoot)
    {
        entity = Entity;
        table = Table;
        schema = Schema;
        columns = Columns;
        keyColumns = KeyColumns;
        viewName = ViewName;
        archiveSubPath = ArchiveSubPath;
        chainToRoot = ChainToRoot;
    }

    private static IReadOnlyList<DuckDBTierColumn> CreateColumnDefinitions(
        IEntityType entity,
        string table,
        string? schema,
        IReadOnlyList<string> columns)
    {
        var store = StoreObjectIdentifier.Table(table, schema);
        var mapped = entity.GetProperties()
            .Select(property => (Property: property, Column: property.GetColumnName(store)))
            .Where(item => item.Column is not null)
            .GroupBy(item => item.Column!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Property, StringComparer.Ordinal);

        return columns.Select(column =>
        {
            var property = mapped[column];
            return new DuckDBTierColumn(
                property.Name,
                column,
                property.GetColumnType(store) ?? property.GetRelationalTypeMapping().StoreType,
                property.IsNullable);
        }).ToArray();
    }
}
