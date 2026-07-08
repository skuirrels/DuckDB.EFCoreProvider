using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

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
        string controlKey,
        TierGranularity granularity,
        string rootTimestampColumn,
        bool includeHotChildFilter)
    {
        Nodes = nodes;
        ControlKey = controlKey;
        Granularity = granularity;
        RootTimestampColumn = rootTimestampColumn;
        IncludeHotChildFilter = includeHotChildFilter;
    }

    /// <summary>The aggregate nodes, root first, every parent before its children.</summary>
    public IReadOnlyList<DuckDBTierNode> Nodes { get; }

    /// <summary>The tier control-table key (shared by the whole aggregate).</summary>
    public string ControlKey { get; }

    /// <summary>The archive partition granularity.</summary>
    public TierGranularity Granularity { get; }

    /// <summary>The physical timestamp column on the root table.</summary>
    public string RootTimestampColumn { get; }

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

        var nodes = new List<DuckDBTierNode> { BuildNode(root, archivePath, []) };

        var children = model.GetEntityTypes()
            .Where(e => e.GetTieredStoreRole() == "Child" && e.GetTieredStoreRoot() == root.Name)
            .Select(child => (Entity: child, Chain: BuildChain(model, child, root)))
            .OrderBy(x => x.Chain.Count); // parents (shorter chains) before their children

        foreach (var (entity, chain) in children)
        {
            nodes.Add(BuildNode(entity, archivePath, chain));
        }

        return new DuckDBTierAggregate(
            nodes, root.GetTieredStoreControlKey()!, root.GetTieredStoreGranularity(), rootTimestampColumn, root.GetTieredStoreHotChildFilter());
    }

    private static DuckDBTierNode BuildNode(IEntityType entity, string archivePath, IReadOnlyList<DuckDBTierControl.TierJoinHop> chain)
    {
        var table = entity.GetTableName()!;
        var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
        var columns = entity.GetProperties()
            .Select(p => p.GetColumnName(store))
            .OfType<string>()
            .ToList();
        var keyColumns = entity.FindPrimaryKey()?.Properties
            .Select(p => p.GetColumnName(store))
            .OfType<string>()
            .ToList()
            ?? [];

        return new DuckDBTierNode(
            entity, table, entity.GetSchema(), columns, keyColumns, entity.GetTieredStoreView(),
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

/// <summary>One resolved node (table) of a tiered aggregate. Internal API.</summary>
public sealed record DuckDBTierNode(
    IEntityType Entity,
    string Table,
    string? Schema,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> KeyColumns,
    string? ViewName,
    string ArchiveSubPath,
    IReadOnlyList<DuckDBTierControl.TierJoinHop> ChainToRoot)
{
    /// <summary><see langword="true" /> for the aggregate root (no chain to a parent).</summary>
    public bool IsRoot => ChainToRoot.Count == 0;
}
