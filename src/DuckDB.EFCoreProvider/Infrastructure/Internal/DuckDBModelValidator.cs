using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DuckDB.EFCoreProvider.Infrastructure.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBModelValidator : RelationalModelValidator
{
    private readonly bool _isDuckLake;

    /// <summary>Creates a native-DuckDB model validator.</summary>
    public DuckDBModelValidator(
        ModelValidatorDependencies dependencies,
        RelationalModelValidatorDependencies relationalDependencies)
        : this(dependencies, relationalDependencies, null)
    {
    }

    public DuckDBModelValidator(
        ModelValidatorDependencies dependencies,
        RelationalModelValidatorDependencies relationalDependencies,
        IDuckLakeSingletonOptions? singletonOptions)
        : base(dependencies, relationalDependencies)
    {
        _isDuckLake = singletonOptions?.IsDuckLake == true;
    }

    /// <inheritdoc />
    public override void Validate(IModel model, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        base.Validate(model, logger);

        ValidateAutoIncrement(model);
        ValidateTieredStores(model);

        if (_isDuckLake)
        {
            ValidateDuckLake(model);
        }
    }

    private static void ValidateDuckLake(IModel model)
    {
        if (model.GetSequences().Any())
        {
            throw new InvalidOperationException(
                "DuckLake does not support sequences. Configure client-assigned keys or a client-side value generator.");
        }

        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.GetTieredStoreRole() is not null)
            {
                throw new InvalidOperationException(
                    $"Entity '{entityType.DisplayName()}' enables provider tiered storage. DuckLake already owns the data-file "
                    + "lifecycle, so the DuckDB tiered-storage feature cannot be combined with a DuckLake profile.");
            }

            foreach (var property in entityType.GetProperties())
            {
                if (property.GetValueGenerationStrategy() == DuckDBValueGenerationStrategy.AutoIncrement)
                {
                    throw new InvalidOperationException(
                        $"DuckLake does not support auto-increment or sequence-backed values. Property "
                        + $"'{entityType.DisplayName()}.{property.Name}' must use a client-assigned value.");
                }

                if (property.GetComputedColumnSql() is not null)
                {
                    throw new InvalidOperationException(
                        $"DuckLake does not support generated columns. Property "
                        + $"'{entityType.DisplayName()}.{property.Name}' must be computed by the application.");
                }

                if (property.GetDefaultValueSql() is not null)
                {
                    throw new InvalidOperationException(
                        $"DuckLake only supports literal defaults. Property '{entityType.DisplayName()}.{property.Name}' "
                        + "uses a SQL default expression; assign the value in the application instead. A literal "
                        + "HasDefaultValue(...) may be retained only with ValueGeneratedNever().");
                }

                if (property.FindAnnotation(RelationalAnnotationNames.DefaultValue) is not null
                    && property.ValueGenerated != ValueGenerated.Never)
                {
                    throw new InvalidOperationException(
                        $"DuckLake can store a literal default for '{entityType.DisplayName()}.{property.Name}', but EF cannot "
                        + "read that generated value back because DuckLake does not support RETURNING. Configure "
                        + "ValueGeneratedNever() and assign the value in tracked writes, or remove the default.");
                }

                if (property.ValueGenerated is ValueGenerated.OnUpdate or ValueGenerated.OnAddOrUpdate)
                {
                    throw new InvalidOperationException(
                        $"DuckLake cannot read store-generated values for '{entityType.DisplayName()}.{property.Name}'. "
                        + "Compute and assign the value in the application instead.");
                }
            }
        }
    }

    private static void ValidateTieredStores(IModel model)
    {
        var rootArchives = new List<(string Path, string Entity)>();
        var roots = model.GetEntityTypes()
            .Where(entityType => entityType.GetTieredStoreRole() == "Root" && entityType.GetTableName() is not null)
            .ToArray();
        ValidateUniqueRootControlKeys(roots);
        ValidateTieredViewNames(model);
        var rootPartitionColumns = roots
            .ToDictionary(
                entityType => entityType.Name,
                entityType => ValidateRootPartitionProperties(
                    entityType,
                    StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema())));

        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.GetTieredStoreRole() is not { } role || entityType.GetTableName() is not { } table)
            {
                continue;
            }

            var storeObject = StoreObjectIdentifier.Table(table, entityType.GetSchema());

            ValidateMatchKey(entityType, storeObject);
            ValidatePartitionOwnership(entityType, role);
            ValidateReadModelColumns(model, entityType, storeObject);

            if (role == "Child")
            {
                var bindings = entityType.GetTieredStoreBindings();
                if (bindings.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Tiered-storage child '{entityType.DisplayName()}' has no relationship binding.");
                }

                var duplicateRoot = bindings
                    .GroupBy(binding => binding.RootEntityName, StringComparer.Ordinal)
                    .FirstOrDefault(group => group.Count() > 1);
                if (duplicateRoot is not null)
                {
                    throw new InvalidOperationException(
                        $"Tiered-storage child '{entityType.DisplayName()}' is configured through more than one "
                        + $"relationship path beneath root '{duplicateRoot.Key}'. Multiple independently archived "
                        + "roots are supported, but multiple paths to the same physical table beneath one root "
                        + "require distinct table mappings.");
                }

                var bindingRoots = new List<IReadOnlyEntityType>(bindings.Count);
                foreach (var binding in bindings)
                {
                    var root = model.FindEntityType(binding.RootEntityName)
                        ?? throw new InvalidOperationException(
                            $"Tiered-storage binding '{binding.BindingId}' on '{entityType.DisplayName()}' cannot "
                            + $"resolve root '{binding.RootEntityName}'.");
                    if (root.GetTieredStoreRole() != "Root")
                    {
                        throw new InvalidOperationException(
                            $"Tiered-storage binding '{binding.BindingId}' on '{entityType.DisplayName()}' references "
                            + $"'{root.DisplayName()}', which is not configured as a tiered root.");
                    }

                    if (!string.Equals(root.GetTieredStoreControlKey(), binding.ControlKey, StringComparison.Ordinal)
                        || !string.Equals(root.GetTieredStoreArchivePath(), binding.ArchivePath, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Tiered-storage binding '{binding.BindingId}' on '{entityType.DisplayName()}' does not "
                            + $"match root '{root.DisplayName()}' archive metadata.");
                    }

                    ValidateChildForeignKey(model, entityType, binding);
                    ValidateChildPartitionColumnCollisions(
                        entityType,
                        storeObject,
                        rootPartitionColumns.GetValueOrDefault(binding.RootEntityName) ?? []);
                    bindingRoots.Add(root);
                }

                ValidateReservedColumns(entityType, storeObject, bindingRoots);
            }
            else
            {
                ValidateLifecycleProperty(entityType, storeObject);
                ValidateReservedColumns(entityType, storeObject, [entityType]);
                rootArchives.Add((NormalizeArchivePath(entityType.GetTieredStoreArchivePath()!), entityType.DisplayName()));
            }
        }

        ValidateNoOverlappingArchivePaths(rootArchives);
        _ = DuckDBTierPartitionPruningMetadata.ResolveAll(model);
    }

    private static void ValidateUniqueRootControlKeys(IReadOnlyList<IEntityType> roots)
    {
        var missing = roots.FirstOrDefault(root => string.IsNullOrWhiteSpace(root.GetTieredStoreControlKey()));
        if (missing is not null)
        {
            throw new InvalidOperationException(
                $"Tiered-storage root '{missing.DisplayName()}' must have a non-empty control key.");
        }

        var duplicate = roots
            .GroupBy(root => root.GetTieredStoreControlKey()!, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is null)
        {
            return;
        }

        var rootNames = string.Join(
            ", ",
            duplicate
                .OrderBy(root => root.Name, StringComparer.Ordinal)
                .Select(root => $"'{root.DisplayName()}'"));
        throw new InvalidOperationException(
            $"Tiered-storage roots {rootNames} use the same control key '{duplicate.Key}'. Each configured root "
            + "must have a unique control key because watermarks, active generations, and archive contracts are "
            + "persisted by that key.");
    }

    private static void ValidateTieredViewNames(IModel model)
    {
        var tieredViews = model.GetEntityTypes()
            .Where(entityType => entityType.GetTieredStoreRole() is not null)
            .Select(entityType => (Entity: entityType, View: entityType.GetTieredStoreView()))
            .Where(entry => entry.View is not null)
            .Select(entry => (entry.Entity, View: entry.View!))
            .ToArray();

        var duplicate = tieredViews
            .GroupBy(entry => entry.View, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Select(entry => entry.Entity.Name).Distinct(StringComparer.Ordinal).Count() > 1);
        if (duplicate is not null)
        {
            var entities = string.Join(
                ", ",
                duplicate.OrderBy(entry => entry.Entity.Name, StringComparer.Ordinal)
                    .Select(entry => $"'{entry.Entity.DisplayName()}'"));
            throw new InvalidOperationException(
                $"Tiered entities {entities} target the same physical view '{duplicate.Key}'. Each physical tiered "
                + "entity must use a distinct view; a descendant shared by multiple roots remains one entity and may "
                + "reuse its one entity-wide view.");
        }

        var tableNames = model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var providerTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DuckDBTierControl.ControlTable,
            DuckDBTierControl.GenerationTable,
            DuckDBTierControl.GenerationNodeTable,
            DuckDBTierControl.GenerationFileTable,
        };

        foreach (var (entity, view) in tieredViews)
        {
            if (tableNames.Contains(view) || providerTables.Contains(view))
            {
                throw new InvalidOperationException(
                    $"Tiered entity '{entity.DisplayName()}' targets view '{view}', but that name is already used by "
                    + "a mapped table or provider-owned tier metadata table. Choose a distinct tiered view name.");
            }
        }
    }

    private static IReadOnlyList<string> ValidateRootPartitionProperties(
        IReadOnlyEntityType root,
        StoreObjectIdentifier storeObject)
    {
        var definitions = root.GetTieredStorePartitionDefinitions();
        if (definitions.Count == 0)
        {
            return [];
        }

        var duplicateProperty = definitions.GroupBy(definition => definition.PropertyName, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateProperty is not null)
        {
            throw new InvalidOperationException(
                $"Tiered-storage root '{root.DisplayName()}' declares partition property '{duplicateProperty}' more "
                + "than once. Each property can occupy only one position in the physical layout.");
        }

        var mappedColumns = root.GetProperties()
            .Select(property => property.GetColumnName(storeObject))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var columns = new List<string>(definitions.Count);
        foreach (var definition in definitions)
        {
            if (definition.PartitionName is not null && string.IsNullOrWhiteSpace(definition.PartitionName))
            {
                throw new InvalidOperationException(
                    $"Tiered-storage partition '{root.DisplayName()}.{definition.PropertyName}' has an empty Hive "
                    + "partition name.");
            }

            var property = root.FindProperty(definition.PropertyName)
                ?? throw new InvalidOperationException(
                    $"Tiered-storage partition property '{root.DisplayName()}.{definition.PropertyName}' is not a mapped scalar "
                    + "property on the aggregate root.");
            var sourceColumn = property.GetColumnName(storeObject)
                ?? throw new InvalidOperationException(
                    $"Tiered-storage partition property '{root.DisplayName()}.{definition.PropertyName}' is not mapped to the "
                    + "root table.");
            if (definition.Transform != TierPartitionTransform.Value && !IsDateProperty(property.ClrType))
            {
                throw new InvalidOperationException(
                    $"Tiered-storage partition '{root.DisplayName()}.{definition.PropertyName}' uses "
                    + $"{definition.Transform} bucketing, but its CLR type '{property.ClrType.ShortDisplayName()}' is not "
                    + "DateTime or DateOnly.");
            }

            var column = definition.ResolveName(sourceColumn);

            if ((definition.Transform != TierPartitionTransform.Value
                    || !string.Equals(column, sourceColumn, StringComparison.OrdinalIgnoreCase))
                && mappedColumns.Contains(column))
            {
                throw new InvalidOperationException(
                    $"Tiered-storage partition '{root.DisplayName()}.{definition.PropertyName}' uses Hive name "
                    + $"'{column}', which collides with a mapped root column. Choose a distinct partition name.");
            }

            if (columns.Contains(column, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Tiered-storage root '{root.DisplayName()}' maps more than one partition property to column "
                    + $"'{column}'. Each additional partition must have a distinct physical column.");
            }

            columns.Add(column);
        }

        var lifecycle = definitions.FirstOrDefault(
            definition => definition.PropertyName == root.GetTieredStoreTimestamp()
                && IsSafeLifecyclePartition(definition.Transform, root.GetTieredStoreGranularity()));
        if (lifecycle is null)
        {
            throw new InvalidOperationException(
                $"Tiered-storage root '{root.DisplayName()}' must include its lifecycle property "
                + $"'{root.GetTieredStoreTimestamp()}' in the ordered partition plan using "
                + (root.GetTieredStoreGranularity() == TierGranularity.Day
                    ? "ByDay(...) (or exact By(...))"
                    : "ByMonth(...), ByDay(...), or exact By(...)")
                + ". This keeps incremental archive windows disjoint and crash-safe.");
        }

        return columns;
    }

    private static bool IsDateProperty(Type clrType)
    {
        clrType = Nullable.GetUnderlyingType(clrType) ?? clrType;
        return clrType == typeof(DateTime) || clrType == typeof(DateOnly);
    }

    private static bool IsSafeLifecyclePartition(
        TierPartitionTransform transform,
        TierGranularity granularity)
        => transform == TierPartitionTransform.Value
           || transform == TierPartitionTransform.Day
           || granularity == TierGranularity.Month && transform == TierPartitionTransform.Month;

    private static void ValidatePartitionOwnership(IReadOnlyEntityType entityType, string role)
    {
        if (role != "Root" && entityType.GetTieredStorePartitionDefinitions().Count > 0)
        {
            throw new InvalidOperationException(
                $"Tiered-storage partitions can only be declared on an aggregate root, but "
                + $"'{entityType.DisplayName()}' has role '{role}'. Declare PartitionBy(...) on the root's "
                + "TieredStoreBuilder instead; children inherit its partition values.");
        }
    }

    private static void ValidateChildPartitionColumnCollisions(
        IReadOnlyEntityType child,
        StoreObjectIdentifier childStore,
        IReadOnlyList<string> rootPartitionColumns)
    {
        if (rootPartitionColumns.Count == 0)
        {
            return;
        }

        var collision = child.GetProperties()
            .Select(property => property.GetColumnName(childStore))
            .OfType<string>()
            .FirstOrDefault(column => rootPartitionColumns.Contains(column, StringComparer.OrdinalIgnoreCase));
        if (collision is not null)
        {
            throw new InvalidOperationException(
                $"Tiered-storage child '{child.DisplayName()}' maps column '{collision}', which collides with an "
                + "additional partition inherited from its aggregate root. Give the root partition a distinct Hive "
                + "name (for example, By(root => root.OwnerId, \"root_owner_id\")) or rename the child column so the "
                + "partition key can be propagated without replacing child data.");
        }
    }

    private static void ValidateLifecycleProperty(IReadOnlyEntityType entityType, StoreObjectIdentifier storeObject)
    {
        var propertyName = entityType.GetTieredStoreTimestamp();
        var property = propertyName is null ? null : entityType.FindProperty(propertyName);
        if (property is null || property.GetColumnName(storeObject) is null)
        {
            throw new InvalidOperationException(
                $"Tiered-storage lifecycle selector '{entityType.DisplayName()}.{propertyName}' must resolve to a "
                + "direct scalar property mapped to the aggregate root table.");
        }

        var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (!IsDateProperty(clrType))
        {
            throw new InvalidOperationException(
                $"Tiered-storage lifecycle property '{entityType.DisplayName()}.{property.Name}' must be DateTime, "
                + $"DateOnly, or a nullable form, but is '{property.ClrType.ShortDisplayName()}'.");
        }
    }

    private static void ValidateMatchKey(IReadOnlyEntityType entityType, StoreObjectIdentifier storeObject)
    {
        if (entityType.FindPrimaryKey() is null)
        {
            throw new InvalidOperationException(
                $"Tiered-storage entity '{entityType.DisplayName()}' must have a primary key. The generated hot/cold "
                + "union views use the key to suppress crash-retry duplicates while keeping late hot rows visible.");
        }

        var configured = entityType.GetTieredStoreMatchProperties();
        var properties = configured.Count == 0
            ? entityType.FindPrimaryKey()!.Properties
            : configured.Select(name => entityType.FindProperty(name)
                ?? throw new InvalidOperationException(
                    $"Tiered-storage match-key property '{entityType.DisplayName()}.{name}' is not a mapped scalar property."))
                .ToArray();

        foreach (var property in properties)
        {
            if (property.GetColumnName(storeObject) is null)
            {
                throw new InvalidOperationException(
                    $"Tiered-storage match-key property '{entityType.DisplayName()}.{property.Name}' is not mapped to "
                    + $"table '{entityType.GetTableName()}'.");
            }
        }

        if (configured.Count == 0
            || entityType.GetTieredStoreMatchKeyUniqueness() == TierMatchKeyUniqueness.ExternallyEnforced)
        {
            return;
        }

        var declaredUnique = entityType.GetKeys().Any(key => key.Properties.SequenceEqual(properties))
            || entityType.GetIndexes().Any(index => index.IsUnique && index.Properties.SequenceEqual(properties));
        if (!declaredUnique)
        {
            throw new InvalidOperationException(
                $"Tiered-storage match key for '{entityType.DisplayName()}' must be a primary key, alternate key, or "
                + "unique index. If uniqueness is enforced outside EF, pass TierMatchKeyUniqueness.ExternallyEnforced "
                + "to MatchBy(...).");
        }
    }

    private static void ValidateReservedColumns(
        IReadOnlyEntityType entityType,
        StoreObjectIdentifier storeObject,
        IEnumerable<IReadOnlyEntityType> roots)
    {
        foreach (var root in roots.DistinctBy(root => root.Name))
        {
            if (root.GetTieredStorePartitionDefinitions().Count > 0)
            {
                // Explicit plans are validated from their resolved physical names above; exact-value root keys
                // intentionally reuse their mapped source column and child collisions are checked separately.
                continue;
            }

            var granularity = root.GetTieredStoreGranularity();
            var reserved = granularity == TierGranularity.Day
                ? new[] { "year", "month", "day" }
                : new[] { "year", "month" };

            foreach (var property in entityType.GetProperties())
            {
                if (property.GetColumnName(storeObject) is { } column
                    && reserved.Contains(column, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Tiered-storage entity '{entityType.DisplayName()}' maps a property to column '{column}', which "
                        + $"collides with the hive partition column DuckDB adds for {granularity} granularity beneath "
                        + $"root '{root.DisplayName()}'. Rename the column (for example with HasColumnName) so it is "
                        + $"none of: {string.Join(", ", reserved)}.");
                }
            }
        }
    }

    private static void ValidateNoOverlappingArchivePaths(IReadOnlyList<(string Path, string Entity)> rootArchives)
    {
        for (var i = 0; i < rootArchives.Count; i++)
        {
            for (var j = i + 1; j < rootArchives.Count; j++)
            {
                if (ArchivePathsOverlap(rootArchives[i].Path, rootArchives[j].Path))
                {
                    throw new InvalidOperationException(
                        $"Tiered-storage aggregates '{rootArchives[i].Entity}' and '{rootArchives[j].Entity}' use overlapping "
                        + $"archive paths ('{rootArchives[i].Path}' and '{rootArchives[j].Path}'). Give each aggregate its own "
                        + "archive directory: the recursive Parquet glob would otherwise read the other aggregate's files.");
                }
            }
        }
    }

    private static void ValidateChildForeignKey(
        IModel model,
        IReadOnlyEntityType child,
        TieredStoreBinding binding)
    {
        var parent = model.FindEntityType(binding.ParentEntityName)
            ?? throw new InvalidOperationException($"Tiered child '{child.DisplayName()}' has no resolvable parent entity.");
        var navigation = parent.FindNavigation(binding.ParentNavigationName)
            ?? throw new InvalidOperationException(
                $"Tiered child navigation '{binding.ParentNavigationName}' was not found on '{parent.DisplayName()}'. "
                + "The .Including(...) navigation must be a real collection navigation with a foreign key to the parent.");

        if (navigation.ForeignKey.Properties.Count == 0
            || navigation.ForeignKey.Properties.Count != navigation.ForeignKey.PrincipalKey.Properties.Count)
        {
            throw new InvalidOperationException(
                $"Tiered child '{child.DisplayName()}' has an invalid foreign key to '{parent.DisplayName()}'. "
                + "The dependent and principal key column counts must be equal and non-zero.");
        }

        if (parent.Name == binding.RootEntityName)
        {
            var rootBindingId = DuckDBTieredStoreBinding.CreateRootBindingId(
                binding.RootEntityName,
                binding.ControlKey);
            if (!string.Equals(binding.ParentBindingId, rootBindingId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Tiered-storage binding '{binding.BindingId}' on '{child.DisplayName()}' has an invalid root "
                    + "parent binding identifier.");
            }

            return;
        }

        var parentBindings = parent.GetTieredStoreBindings()
            .Where(candidate => candidate.RootEntityName == binding.RootEntityName)
            .ToArray();
        if (!parentBindings.Any(candidate => candidate.BindingId == binding.ParentBindingId)
            && parentBindings.Length != 1)
        {
            throw new InvalidOperationException(
                $"Tiered-storage binding '{binding.BindingId}' on '{child.DisplayName()}' cannot resolve parent "
                + $"binding '{binding.ParentBindingId}' on '{parent.DisplayName()}'.");
        }
    }

    private static void ValidateReadModelColumns(IModel model, IReadOnlyEntityType hot, StoreObjectIdentifier hotStore)
    {
        if (hot.GetTieredStoreView() is not { } view)
        {
            return;
        }

        var hotColumns = hot.GetProperties()
            .Select(p => p.GetColumnName(hotStore))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var viewStore = StoreObjectIdentifier.View(view, hot.GetSchema());
        foreach (var readModel in model.GetEntityTypes().Where(e => e.GetViewName() == view && e.GetTieredStoreRole() is null))
        {
            foreach (var property in readModel.GetProperties())
            {
                if (property.GetColumnName(viewStore) is { } column && !hotColumns.Contains(column))
                {
                    throw new InvalidOperationException(
                        $"Tiered read model '{readModel.DisplayName()}' maps property '{property.Name}' to column '{column}', "
                        + $"which the source entity '{hot.DisplayName()}' does not have. A read model's columns must mirror the "
                        + "hot entity's mapped columns.");
                }
            }
        }
    }

    private static string NormalizeArchivePath(string path)
        => path.Replace('\\', '/').TrimEnd('/');

    private static bool ArchivePathsOverlap(string a, string b)
        => string.Equals(a, b, StringComparison.Ordinal)
           || a.StartsWith(b + "/", StringComparison.Ordinal)
           || b.StartsWith(a + "/", StringComparison.Ordinal);

    private static void ValidateAutoIncrement(IModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var property in entityType.GetDeclaredProperties())
            {
                if (property.GetValueGenerationStrategy() != DuckDBValueGenerationStrategy.AutoIncrement)
                {
                    continue;
                }

                var clrType = property.ClrType.UnwrapNullableType();
                if (!DuckDBValueGenerationStrategyCompatibility.IsAutoIncrementCompatible(clrType))
                {
                    throw new InvalidOperationException(
                        $"DuckDB auto-increment value generation can only be configured for integer properties. Property '{entityType.DisplayName()}.{property.Name}' is '{property.ClrType.Name}'.");
                }

                if (property.GetTypeMapping().Converter != null)
                {
                    throw new InvalidOperationException(
                        $"DuckDB auto-increment value generation cannot be configured for property '{entityType.DisplayName()}.{property.Name}' because it has a value converter.");
                }
            }
        }
    }
}