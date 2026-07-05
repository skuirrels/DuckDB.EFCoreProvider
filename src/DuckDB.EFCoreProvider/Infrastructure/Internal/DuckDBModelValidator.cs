using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
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
    public DuckDBModelValidator(ModelValidatorDependencies dependencies, RelationalModelValidatorDependencies relationalDependencies) : base(dependencies, relationalDependencies)
    {
    }

    /// <inheritdoc />
    public override void Validate(IModel model, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        base.Validate(model, logger);

        ValidateAutoIncrement(model);
        ValidateTieredStores(model);
    }

    private static void ValidateTieredStores(IModel model)
    {
        var rootArchives = new List<(string Path, string Entity)>();

        foreach (var entityType in model.GetEntityTypes())
        {
            var role = entityType.GetTieredStoreRole();
            if (role is null || entityType.GetTableName() is not { } table)
            {
                continue;
            }

            var granularity = ResolveGranularity(model, entityType);
            var reserved = granularity == TierGranularity.Day
                ? new[] { "year", "month", "day" }
                : new[] { "year", "month" };

            var storeObject = StoreObjectIdentifier.Table(table, entityType.GetSchema());
            foreach (var property in entityType.GetProperties())
            {
                if (property.GetColumnName(storeObject) is { } column
                    && reserved.Contains(column, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Tiered-storage entity '{entityType.DisplayName()}' maps a property to column '{column}', which "
                        + $"collides with the hive partition column DuckDB adds for {granularity} granularity. Rename the "
                        + $"column (for example with HasColumnName) so it is none of: {string.Join(", ", reserved)}.");
                }
            }

            if (role == "Child")
            {
                ValidateChildForeignKey(model, entityType);
            }
            else
            {
                rootArchives.Add((NormalizeArchivePath(entityType.GetTieredStoreArchivePath()!), entityType.DisplayName()));
            }

            ValidateReadModelColumns(model, entityType, storeObject);
        }

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

    private static TierGranularity ResolveGranularity(IModel model, IReadOnlyEntityType entityType)
        => entityType.GetTieredStoreRole() == "Root"
            ? entityType.GetTieredStoreGranularity()
            : model.FindEntityType(entityType.GetTieredStoreRoot()!)?.GetTieredStoreGranularity() ?? TierGranularity.Month;

    private static void ValidateChildForeignKey(IModel model, IReadOnlyEntityType child)
    {
        var parent = model.FindEntityType(child.GetTieredStoreParent()!)
            ?? throw new InvalidOperationException($"Tiered child '{child.DisplayName()}' has no resolvable parent entity.");
        var navigation = parent.FindNavigation(child.GetTieredStoreParentNavigation()!)
            ?? throw new InvalidOperationException(
                $"Tiered child navigation '{child.GetTieredStoreParentNavigation()}' was not found on '{parent.DisplayName()}'. "
                + "The .Including(...) navigation must be a real collection navigation with a foreign key to the parent.");

        if (navigation.ForeignKey.Properties.Count != 1)
        {
            throw new InvalidOperationException(
                $"Tiered child '{child.DisplayName()}' must have a single-column foreign key to '{parent.DisplayName()}'; "
                + "composite foreign keys are not supported by tiered storage.");
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
