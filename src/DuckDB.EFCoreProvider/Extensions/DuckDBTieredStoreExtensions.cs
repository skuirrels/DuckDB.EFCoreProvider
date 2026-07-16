using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     Configuration for DuckDB <em>tiered storage</em>: recent ("hot") rows live in normal writable DuckDB
///     tables while older ("cold") rows are offloaded to hive-partitioned Parquet, unified for reporting by
///     generated views.
/// </summary>
/// <remarks>
///     <para>
///         The <em>hot</em> side is your ordinary EF Core model — regular entities with normal keys,
///         relationships, <c>SaveChanges</c>, and <c>Include</c>. <c>ToTieredStore</c> only
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
        => ConfigureTieredStore(modelBuilder, timestamp, archivePath, granularity, controlKey);

    /// <summary>
    ///     Marks an existing entity as a tiered-storage aggregate root using a nullable lifecycle property.
    ///     A <see langword="null" /> lifecycle value remains hot and is not selected for archival.
    /// </summary>
    public static TieredStoreBuilder<TRoot> ToTieredStore<TRoot>(
        this ModelBuilder modelBuilder,
        Expression<Func<TRoot, DateTime?>> timestamp,
        string archivePath,
        TierGranularity granularity = TierGranularity.Month,
        string? controlKey = null)
        where TRoot : class
        => ConfigureTieredStore(modelBuilder, timestamp, archivePath, granularity, controlKey);

    /// <summary>Marks an aggregate root using a <see cref="DateOnly" /> lifecycle property.</summary>
    public static TieredStoreBuilder<TRoot> ToTieredStore<TRoot>(
        this ModelBuilder modelBuilder,
        Expression<Func<TRoot, DateOnly>> timestamp,
        string archivePath,
        TierGranularity granularity = TierGranularity.Month,
        string? controlKey = null)
        where TRoot : class
        => ConfigureTieredStore(modelBuilder, timestamp, archivePath, granularity, controlKey);

    /// <summary>
    ///     Marks an aggregate root using a nullable <see cref="DateOnly" /> lifecycle property.
    ///     A <see langword="null" /> value remains hot.
    /// </summary>
    public static TieredStoreBuilder<TRoot> ToTieredStore<TRoot>(
        this ModelBuilder modelBuilder,
        Expression<Func<TRoot, DateOnly?>> timestamp,
        string archivePath,
        TierGranularity granularity = TierGranularity.Month,
        string? controlKey = null)
        where TRoot : class
        => ConfigureTieredStore(modelBuilder, timestamp, archivePath, granularity, controlKey);

    private static TieredStoreBuilder<TRoot> ConfigureTieredStore<TRoot, TTimestamp>(
        ModelBuilder modelBuilder,
        Expression<Func<TRoot, TTimestamp>> timestamp,
        string archivePath,
        TierGranularity granularity,
        string? controlKey)
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

    /// <summary>Gets the ordered application-declared Hive partition property names on a tiered aggregate root.</summary>
    public static IReadOnlyList<string> GetTieredStorePartitionProperties(this IReadOnlyEntityType entityType)
        => entityType.GetTieredStorePartitionDefinitions()
            .Where(definition => !definition.IsImplicit)
            .Select(definition => definition.PropertyName)
            .ToArray();

    internal static IReadOnlyList<DuckDBTierPartitionDefinition> GetTieredStorePartitionDefinitions(
        this IReadOnlyEntityType entityType)
        => DuckDBTierPartitionDefinitionSerializer.Deserialize(
            entityType.FindAnnotation(DuckDBAnnotationNames.TieredStorePartitionProperties)?.Value as string);

    internal static void SetTieredStorePartitionDefinitions(
        this IMutableEntityType entityType,
        IReadOnlyList<DuckDBTierPartitionDefinition> definitions)
        => entityType.SetAnnotation(
            DuckDBAnnotationNames.TieredStorePartitionProperties,
            DuckDBTierPartitionDefinitionSerializer.Serialize(definitions));

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

    /// <summary>
    ///     Gets the configured match-key property names. An empty result means the entity's primary key is used.
    /// </summary>
    public static IReadOnlyList<string> GetTieredStoreMatchProperties(this IReadOnlyEntityType entityType)
        => entityType.FindAnnotation(DuckDBAnnotationNames.TieredStoreMatchKey)?.Value is string json
            ? JsonSerializer.Deserialize<string[]>(json) ?? []
            : [];

    /// <summary>Gets how uniqueness of the configured match key is guaranteed.</summary>
    public static TierMatchKeyUniqueness GetTieredStoreMatchKeyUniqueness(this IReadOnlyEntityType entityType)
        => entityType.FindAnnotation(DuckDBAnnotationNames.TieredStoreMatchKeyUniqueness)?.Value is string name
           && Enum.TryParse<TierMatchKeyUniqueness>(name, out var uniqueness)
            ? uniqueness
            : TierMatchKeyUniqueness.Model;

    internal static void SetTieredStoreMatchKey(
        this IMutableEntityType entityType,
        IReadOnlyList<string> properties,
        TierMatchKeyUniqueness uniqueness)
    {
        if (!Enum.IsDefined(uniqueness))
        {
            throw new ArgumentOutOfRangeException(nameof(uniqueness), uniqueness, null);
        }

        entityType.SetAnnotation(DuckDBAnnotationNames.TieredStoreMatchKey, JsonSerializer.Serialize(properties));
        entityType.SetAnnotation(DuckDBAnnotationNames.TieredStoreMatchKeyUniqueness, uniqueness.ToString());
    }

    internal static string GetPropertyName<TEntity, TValue>(Expression<Func<TEntity, TValue>> expression)
        => GetDirectMember(
            expression,
            nameof(expression),
            "The selector must be a simple property access, for example 'e => e.Timestamp'.").Name;

    internal static MemberInfo GetDirectMember<TEntity, TValue>(
        Expression<Func<TEntity, TValue>> expression,
        string parameterName,
        string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var body = expression.Body is UnaryExpression
        {
            NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked,
            Operand: var operand,
        }
            ? operand
            : expression.Body;

        return body is MemberExpression { Expression: ParameterExpression } member
            ? member.Member
            : throw new ArgumentException(errorMessage, parameterName);
    }

    internal static IReadOnlyList<string> GetPropertyNames<TEntity>(Expression<Func<TEntity, object?>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var body = UnwrapConvert(expression.Body);
        MemberExpression[] members = body switch
        {
            MemberExpression member => [member],
            NewExpression @new => @new.Arguments.Select(UnwrapConvert).OfType<MemberExpression>().ToArray(),
            _ => [],
        };

        if (members.Length == 0
            || body is NewExpression newExpression && members.Length != newExpression.Arguments.Count
            || members.Any(member => member.Expression is not ParameterExpression))
        {
            throw new ArgumentException(
                "The match-key selector must contain direct property accesses, for example "
                + "'e => e.ExternalId' or 'e => new { e.ParentId, e.Sequence }'.",
                nameof(expression));
        }

        var names = members.Select(member => member.Member.Name).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
        {
            throw new ArgumentException("A match-key property can only be selected once.", nameof(expression));
        }

        return names;
    }

    private static Expression UnwrapConvert(Expression expression)
        => expression is UnaryExpression
        {
            NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked,
            Operand: var operand,
        }
            ? operand
            : expression;
}
