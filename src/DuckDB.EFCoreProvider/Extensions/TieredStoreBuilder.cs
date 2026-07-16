using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     Configures a tiered-storage aggregate root: its cold-reporting read model and its aggregate children.
/// </summary>
/// <typeparam name="TRoot">The aggregate-root entity type.</typeparam>
public sealed class TieredStoreBuilder<TRoot>
    where TRoot : class
{
    private readonly ModelBuilder _modelBuilder;
    private readonly IMutableEntityType _root;
    private readonly string _archivePath;
    private readonly string _controlKey;

    internal TieredStoreBuilder(ModelBuilder modelBuilder, IMutableEntityType root, string archivePath, string controlKey)
    {
        _modelBuilder = modelBuilder;
        _root = root;
        _archivePath = archivePath;
        _controlKey = controlKey;
    }

    /// <summary>
    ///     Declares the keyless read-model type used to query the root's hot table and cold archive together.
    ///     Its properties must mirror the hot entity's mapped columns.
    /// </summary>
    public TieredStoreBuilder<TRoot> WithReadModel<TReadModel>()
        where TReadModel : class
    {
        DuckDBTieredStoreExtensions.MapReadModel(_modelBuilder, _root, typeof(TReadModel));
        return this;
    }

    /// <summary>
    ///     Selects the stable property or composite properties used to match hot and cold root representations.
    ///     The primary key remains the default when this method is not called.
    /// </summary>
    public TieredStoreBuilder<TRoot> MatchBy(
        Expression<Func<TRoot, object?>> key,
        TierMatchKeyUniqueness uniqueness = TierMatchKeyUniqueness.Model)
    {
        _root.SetTieredStoreMatchKey(DuckDBTieredStoreExtensions.GetPropertyNames(key), uniqueness);
        return this;
    }

    /// <summary>
    ///     Adds mapped root properties as exact-value Hive partition keys, in declaration order. This shorthand
    ///     retains the configured lifecycle date bucket as the final safety partition; use the ordered-builder
    ///     overload to place that bucket explicitly elsewhere. Every child archive inherits the root values.
    /// </summary>
    /// <param name="properties">Direct scalar property accesses on the aggregate root.</param>
    public TieredStoreBuilder<TRoot> PartitionBy(params Expression<Func<TRoot, object?>>[] properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (properties.Length == 0)
        {
            throw new ArgumentException("At least one partition property is required.", nameof(properties));
        }

        var additions = properties.Select(property =>
        {
            ArgumentNullException.ThrowIfNull(property);
            return new DuckDBTierPartitionDefinition(
                DuckDBTieredStoreExtensions.GetPropertyName(property),
                TierPartitionTransform.Value);
        }).ToArray();
        if (additions.Select(definition => definition.PropertyName).Distinct(StringComparer.Ordinal).Count()
            != additions.Length)
        {
            throw new ArgumentException("A tiered-store partition property can only be declared once.", nameof(properties));
        }

        var definitions = _root.GetTieredStorePartitionDefinitions().ToList();
        foreach (var addition in additions)
        {
            var existingIndex = definitions.FindIndex(
                definition => definition.PropertyName == addition.PropertyName);
            if (existingIndex < 0)
            {
                continue;
            }

            if (definitions[existingIndex].IsImplicit)
            {
                // A later exact declaration of the lifecycle property replaces the shorthand's hidden bucket.
                definitions.RemoveAt(existingIndex);
            }
            else
            {
                throw new ArgumentException(
                    "A tiered-store partition property can only be declared once.",
                    nameof(properties));
            }
        }

        var insertionIndex = definitions.FindIndex(definition => definition.IsImplicit);
        if (insertionIndex < 0)
        {
            insertionIndex = definitions.Count;
        }

        definitions.InsertRange(insertionIndex, additions);
        var lifecycleProperty = _root.GetTieredStoreTimestamp()!;
        if (!definitions.Any(definition => definition.IsImplicit)
            && !definitions.Any(definition => definition.PropertyName == lifecycleProperty))
        {
            definitions.Add(new DuckDBTierPartitionDefinition(
                lifecycleProperty,
                _root.GetTieredStoreGranularity() == TierGranularity.Day
                    ? TierPartitionTransform.Day
                    : TierPartitionTransform.Month,
                IsImplicit: true));
        }

        _root.SetTieredStorePartitionDefinitions(definitions);
        return this;
    }

    /// <summary>
    ///     Replaces the physical Hive layout with an application-defined ordered partition plan. Use
    ///     <see cref="TieredPartitionBuilder{TRoot}.By{TProperty}" /> for exact values and
    ///     <see cref="TieredPartitionBuilder{TRoot}.ByMonth{TProperty}" />,
    ///     <see cref="TieredPartitionBuilder{TRoot}.ByDay{TProperty}" />, or
    ///     <see cref="TieredPartitionBuilder{TRoot}.ByYear{TProperty}" /> for date buckets.
    /// </summary>
    public TieredStoreBuilder<TRoot> PartitionBy(Action<TieredPartitionBuilder<TRoot>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new TieredPartitionBuilder<TRoot>();
        configure(builder);
        if (builder.Definitions.Count == 0)
        {
            throw new ArgumentException("At least one partition must be declared.", nameof(configure));
        }

        _root.SetTieredStorePartitionDefinitions(builder.Definitions);
        return this;
    }

    /// <summary>
    ///     Disables the "root is hot" semijoin guard on this aggregate's child views. By default a child row is
    ///     shown as hot only if its root is on the hot side of the watermark, which keeps reads correct in the
    ///     brief window between a crashed archive's <c>COPY</c> and its delete. Disabling it makes child views a
    ///     plain <c>SELECT * FROM child</c> (faster, especially for deep aggregates) at the cost of a transient
    ///     double-count if the process dies mid-archive; the next archive still self-heals.
    /// </summary>
    public TieredStoreBuilder<TRoot> WithoutHotChildFilter()
    {
        _root.SetAnnotation(DuckDBAnnotationNames.TieredStoreHotChildFilter, false);
        return this;
    }

    /// <summary>
    ///     Declares a collection navigation as an aggregate child that is archived and purged together with the
    ///     root. Nest <see cref="TieredChildBuilder{TChild}.Including{TGrandchild}" /> for deeper aggregates.
    /// </summary>
    public TieredStoreBuilder<TRoot> Including<TChild>(
        Expression<Func<TRoot, IEnumerable<TChild>>> navigation,
        Action<TieredChildBuilder<TChild>>? configure = null)
        where TChild : class
    {
        var navigationName = DuckDBTieredStoreExtensions.GetPropertyName(navigation);
        var child = DuckDBTieredStoreExtensions.AddChild(
            _modelBuilder, _root, _root, typeof(TChild), navigationName, _archivePath, _controlKey);
        configure?.Invoke(new TieredChildBuilder<TChild>(_modelBuilder, child, _root, _archivePath, _controlKey));
        return this;
    }
}

/// <summary>
///     Builds an ordered, root-owned physical partition plan for a tiered Parquet archive.
/// </summary>
public sealed class TieredPartitionBuilder<TRoot>
    where TRoot : class
{
    private readonly List<DuckDBTierPartitionDefinition> _definitions = [];

    internal IReadOnlyList<DuckDBTierPartitionDefinition> Definitions => _definitions;

    /// <summary>Adds an exact mapped property value at this position in the directory hierarchy.</summary>
    public TieredPartitionBuilder<TRoot> By<TProperty>(Expression<Func<TRoot, TProperty>> property)
        => Add(property, TierPartitionTransform.Value);

    /// <summary>Adds a calendar-year bucket for a mapped date/time property at this position.</summary>
    public TieredPartitionBuilder<TRoot> ByYear<TProperty>(Expression<Func<TRoot, TProperty>> property)
        => Add(property, TierPartitionTransform.Year);

    /// <summary>Adds a calendar-month bucket for a mapped date/time property at this position.</summary>
    public TieredPartitionBuilder<TRoot> ByMonth<TProperty>(Expression<Func<TRoot, TProperty>> property)
        => Add(property, TierPartitionTransform.Month);

    /// <summary>Adds a calendar-day bucket for a mapped date/time property at this position.</summary>
    public TieredPartitionBuilder<TRoot> ByDay<TProperty>(Expression<Func<TRoot, TProperty>> property)
        => Add(property, TierPartitionTransform.Day);

    private TieredPartitionBuilder<TRoot> Add<TProperty>(
        Expression<Func<TRoot, TProperty>> property,
        TierPartitionTransform transform)
    {
        var propertyName = DuckDBTieredStoreExtensions.GetPropertyName(property);
        if (_definitions.Any(definition => definition.PropertyName == propertyName))
        {
            throw new ArgumentException(
                $"Tiered-store partition property '{propertyName}' can only be declared once.",
                nameof(property));
        }

        _definitions.Add(new DuckDBTierPartitionDefinition(propertyName, transform));
        return this;
    }
}

/// <summary>
///     Configures a tiered-storage aggregate child: its cold-reporting read model and any deeper children.
/// </summary>
/// <typeparam name="TChild">The child entity type.</typeparam>
public sealed class TieredChildBuilder<TChild>
    where TChild : class
{
    private readonly ModelBuilder _modelBuilder;
    private readonly IMutableEntityType _child;
    private readonly IMutableEntityType _root;
    private readonly string _archivePath;
    private readonly string _controlKey;

    internal TieredChildBuilder(ModelBuilder modelBuilder, IMutableEntityType child, IMutableEntityType root, string archivePath, string controlKey)
    {
        _modelBuilder = modelBuilder;
        _child = child;
        _root = root;
        _archivePath = archivePath;
        _controlKey = controlKey;
    }

    /// <summary>Declares the keyless read-model type used to query this child's hot table and cold archive together.</summary>
    public TieredChildBuilder<TChild> WithReadModel<TReadModel>()
        where TReadModel : class
    {
        DuckDBTieredStoreExtensions.MapReadModel(_modelBuilder, _child, typeof(TReadModel));
        return this;
    }

    /// <summary>
    ///     Selects the stable property or composite properties used to match hot and cold child representations.
    ///     The primary key remains the default when this method is not called.
    /// </summary>
    public TieredChildBuilder<TChild> MatchBy(
        Expression<Func<TChild, object?>> key,
        TierMatchKeyUniqueness uniqueness = TierMatchKeyUniqueness.Model)
    {
        _child.SetTieredStoreMatchKey(DuckDBTieredStoreExtensions.GetPropertyNames(key), uniqueness);
        return this;
    }

    /// <summary>Declares a deeper aggregate child (grandchild of the root).</summary>
    public TieredChildBuilder<TChild> Including<TGrandchild>(
        Expression<Func<TChild, IEnumerable<TGrandchild>>> navigation,
        Action<TieredChildBuilder<TGrandchild>>? configure = null)
        where TGrandchild : class
    {
        var navigationName = DuckDBTieredStoreExtensions.GetPropertyName(navigation);
        var grandchild = DuckDBTieredStoreExtensions.AddChild(
            _modelBuilder, _child, _root, typeof(TGrandchild), navigationName, _archivePath, _controlKey);
        configure?.Invoke(new TieredChildBuilder<TGrandchild>(_modelBuilder, grandchild, _root, _archivePath, _controlKey));
        return this;
    }
}