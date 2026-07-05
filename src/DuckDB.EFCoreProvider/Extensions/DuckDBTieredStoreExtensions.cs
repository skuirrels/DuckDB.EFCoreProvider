using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     Configuration for DuckDB <em>tiered storage</em>: recent ("hot") rows live in normal writable DuckDB
///     tables while older ("cold") rows are offloaded to hive-partitioned Parquet, unified for reporting by
///     generated views.
/// </summary>
/// <remarks>
///     <para>
///         The <em>hot</em> side is your ordinary EF Core model — regular entities with normal keys,
///         relationships, <c>SaveChanges</c>, and <c>Include</c>. <see cref="ToTieredStore{TRoot}" /> only
///         annotates an existing entity as a tiered aggregate root and (via <see cref="TieredStoreBuilder{TRoot}" />)
///         declares its aggregate children and the read-model types used to query hot+cold together.
///     </para>
///     <para>
///         The <em>cold/reporting</em> side uses a separate keyless read-model type per table, mapped to a
///         union view. You query it as an ordinary keyless <see cref="DbSet{TEntity}" /> and join read-models
///         with LINQ; the view transparently spans the hot table and the Parquet archive.
///     </para>
/// </remarks>
public static class DuckDBTieredStoreExtensions
{
    /// <summary>
    ///     Marks an existing entity as a tiered-storage aggregate root, offloaded to Parquet under
    ///     <paramref name="archivePath" /> and partitioned on <paramref name="timestamp" />.
    /// </summary>
    /// <typeparam name="TRoot">The aggregate-root entity (an ordinary EF Core entity).</typeparam>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="timestamp">The temporal property that partitions the aggregate between hot and cold.</param>
    /// <param name="archivePath">The root directory of the cold Parquet archive.</param>
    /// <param name="granularity">The archive partition granularity. Defaults to <see cref="TierGranularity.Month" />.</param>
    /// <param name="controlKey">The tier control-table key. Defaults to the root table name.</param>
    /// <returns>A builder for declaring the read model and aggregate children.</returns>
    public static TieredStoreBuilder<TRoot> ToTieredStore<TRoot>(
        this ModelBuilder modelBuilder,
        Expression<Func<TRoot, DateTime>> timestamp,
        string archivePath,
        TierGranularity granularity = TierGranularity.Month,
        string? controlKey = null)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(timestamp);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        var entity = modelBuilder.Entity<TRoot>();
        var table = entity.Metadata.GetTableName() ?? typeof(TRoot).Name;
        var key = controlKey ?? table;

        entity.Metadata.SetAnnotation(DuckDBAnnotationNames.TieredStoreRole, "Root");
        entity.Metadata.SetAnnotation(DuckDBAnnotationNames.TieredStoreArchivePath, archivePath);
        entity.Metadata.SetAnnotation(DuckDBAnnotationNames.TieredStoreTimestamp, GetPropertyName(timestamp));
        entity.Metadata.SetAnnotation(DuckDBAnnotationNames.TieredStoreGranularity, granularity.ToString());
        entity.Metadata.SetAnnotation(DuckDBAnnotationNames.TieredStoreControlKey, key);

        return new TieredStoreBuilder<TRoot>(modelBuilder, entity.Metadata, archivePath, key);
    }

    internal static void MapReadModel(ModelBuilder modelBuilder, IMutableEntityType hot, Type readModel)
    {
        var table = hot.GetTableName() ?? hot.ClrType.Name;
        var view = table + "_tiered";
        hot.SetAnnotation(DuckDBAnnotationNames.TieredStoreView, view);
        modelBuilder.Entity(readModel).HasNoKey().ToView(view);
    }

    internal static IMutableEntityType AddChild(
        ModelBuilder modelBuilder,
        IMutableEntityType parent,
        IMutableEntityType root,
        Type childClrType,
        string navigationName,
        string archivePath,
        string controlKey)
    {
        var child = modelBuilder.Entity(childClrType).Metadata;
        child.SetAnnotation(DuckDBAnnotationNames.TieredStoreRole, "Child");
        child.SetAnnotation(DuckDBAnnotationNames.TieredStoreParent, parent.Name);
        child.SetAnnotation(DuckDBAnnotationNames.TieredStoreRoot, root.Name);
        child.SetAnnotation(DuckDBAnnotationNames.TieredStoreParentNavigation, navigationName);
        child.SetAnnotation(DuckDBAnnotationNames.TieredStoreArchivePath, archivePath);
        child.SetAnnotation(DuckDBAnnotationNames.TieredStoreControlKey, controlKey);
        return child;
    }

    /// <summary>Gets the tiered-storage role (<c>Root</c>/<c>Child</c>) of an entity, or <see langword="null" />.</summary>
    public static string? GetTieredStoreRole(this IReadOnlyEntityType entityType)
        => entityType.FindAnnotation(DuckDBAnnotationNames.TieredStoreRole)?.Value as string;

    /// <summary>Gets the cold-archive root directory for a tiered entity, or <see langword="null" />.</summary>
    public static string? GetTieredStoreArchivePath(this IReadOnlyEntityType entityType)
        => entityType.FindAnnotation(DuckDBAnnotationNames.TieredStoreArchivePath)?.Value as string;

    /// <summary>Gets the temporal property name of a tiered root, or <see langword="null" />.</summary>
    public static string? GetTieredStoreTimestamp(this IReadOnlyEntityType entityType)
        => entityType.FindAnnotation(DuckDBAnnotationNames.TieredStoreTimestamp)?.Value as string;

    /// <summary>Gets the archive partition granularity for a tiered entity. Defaults to <see cref="TierGranularity.Month" />.</summary>
    public static TierGranularity GetTieredStoreGranularity(this IReadOnlyEntityType entityType)
        => entityType.FindAnnotation(DuckDBAnnotationNames.TieredStoreGranularity)?.Value is string name
           && Enum.TryParse<TierGranularity>(name, out var granularity)
            ? granularity
            : TierGranularity.Month;

    /// <summary>Gets the union view name for a tiered entity, or <see langword="null" /> if it has no read model.</summary>
    public static string? GetTieredStoreView(this IReadOnlyEntityType entityType)
        => entityType.FindAnnotation(DuckDBAnnotationNames.TieredStoreView)?.Value as string;

    /// <summary>Gets the tier control-table key for a tiered entity, or <see langword="null" />.</summary>
    public static string? GetTieredStoreControlKey(this IReadOnlyEntityType entityType)
        => entityType.FindAnnotation(DuckDBAnnotationNames.TieredStoreControlKey)?.Value as string;

    /// <summary>Gets the immediate parent hot entity name of a tiered child, or <see langword="null" />.</summary>
    public static string? GetTieredStoreParent(this IReadOnlyEntityType entityType)
        => entityType.FindAnnotation(DuckDBAnnotationNames.TieredStoreParent)?.Value as string;

    /// <summary>Gets the aggregate-root hot entity name of a tiered child, or <see langword="null" />.</summary>
    public static string? GetTieredStoreRoot(this IReadOnlyEntityType entityType)
        => entityType.FindAnnotation(DuckDBAnnotationNames.TieredStoreRoot)?.Value as string;

    /// <summary>Gets the parent navigation name that points to a tiered child, or <see langword="null" />.</summary>
    public static string? GetTieredStoreParentNavigation(this IReadOnlyEntityType entityType)
        => entityType.FindAnnotation(DuckDBAnnotationNames.TieredStoreParentNavigation)?.Value as string;

    /// <summary>
    ///     Gets whether child union views include the "root is hot" semijoin guard. Defaults to
    ///     <see langword="true" />.
    /// </summary>
    public static bool GetTieredStoreHotChildFilter(this IReadOnlyEntityType entityType)
        => entityType.FindAnnotation(DuckDBAnnotationNames.TieredStoreHotChildFilter)?.Value as bool? ?? true;

    internal static string GetPropertyName<TEntity, TValue>(Expression<Func<TEntity, TValue>> expression)
        => expression.Body switch
        {
            MemberExpression member => member.Member.Name,
            UnaryExpression { Operand: MemberExpression member } => member.Member.Name,
            _ => throw new ArgumentException(
                "The selector must be a simple property access, for example 'e => e.Timestamp'.",
                nameof(expression)),
        };
}
