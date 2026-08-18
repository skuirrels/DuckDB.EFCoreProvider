using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace DuckDB.EFCoreProvider.Extensions.Internal;

internal sealed record DuckDBUpsertPlan(
    string Table,
    string? Schema,
    DuckDBUpsertStrategy Strategy,
    ImmutableArray<string> InsertColumns,
    ImmutableArray<string> UpdateColumns,
    ImmutableArray<string> ConflictColumns,
    bool RequiresTargetCardinalityValidation,
    ImmutableArray<DuckDBUpsertValueAccessor> ValueAccessors,
    Action<IDuckDBAppenderRow, object>? WriteRow)
{
    internal int ColumnCount => InsertColumns.Length;
}

internal static class DuckDBUpsertPlanner
{
    private static readonly ConditionalWeakTable<
        IEntityType,
        ConcurrentDictionary<UpsertPlanCacheKey, DuckDBUpsertPlan>> PlanCaches = new();

    internal static DuckDBUpsertPlan GetOrCreate(
        DbContext context,
        Type clrType,
        IReadOnlyList<string>? conflictPropertyNames = null,
        DuckDBUpsertMatchMode matchMode = DuckDBUpsertMatchMode.UniqueConflictTarget)
    {
        var entityType = context.Model.FindEntityType(clrType)
            ?? throw new InvalidOperationException($"'{clrType.Name}' is not part of the model.");

        var table = entityType.GetTableName()
            ?? throw new InvalidOperationException($"'{clrType.Name}' is not mapped to a table; upsert is not supported.");
        var schema = entityType.GetSchema();
        var capabilities = context.GetService<IDuckDBEngineCapabilities>();
        var conflictTarget = ResolveConflictTarget(entityType, conflictPropertyNames, matchMode);
        var strategy = matchMode == DuckDBUpsertMatchMode.LogicalKeyMerge
            ? DuckDBUpsertStrategy.Merge
            : capabilities.UpsertStrategy;
        var requiresTargetCardinalityValidation = strategy == DuckDBUpsertStrategy.Merge
            && !(capabilities.SupportsSchemaConstraints && conflictTarget.IsPhysicallyEnforced);
        var usesRemoteValueRendering = capabilities.SupportsRemoteCommandExecution;
        var planCache = PlanCaches.GetValue(entityType, static _ => new());
        var cacheKey = new UpsertPlanCacheKey(
            schema,
            table,
            strategy,
            requiresTargetCardinalityValidation,
            usesRemoteValueRendering,
            conflictTarget.MetadataIdentity);

        return planCache.GetOrAdd(
            cacheKey,
            static (_, state) => BuildPlan(
                state.EntityType,
                state.Table,
                state.Schema,
                state.Strategy,
                state.RequiresTargetCardinalityValidation,
                state.UsesRemoteValueRendering,
                state.ConflictTarget),
            (EntityType: entityType,
                Table: table,
                Schema: schema,
                Strategy: strategy,
                RequiresTargetCardinalityValidation: requiresTargetCardinalityValidation,
                UsesRemoteValueRendering: usesRemoteValueRendering,
                ConflictTarget: conflictTarget));
    }

    private static UpsertConflictTarget ResolveConflictTarget(
        IEntityType entityType,
        IReadOnlyList<string>? conflictPropertyNames,
        DuckDBUpsertMatchMode matchMode)
    {
        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"'{entityType.ClrType.Name}' has no primary key; upsert requires a primary key.");

        if (conflictPropertyNames is null)
        {
            return new UpsertConflictTarget(primaryKey.Properties, primaryKey, UsesPrimaryKey: true, IsPhysicallyEnforced: true);
        }

        var properties = conflictPropertyNames.Select(name => entityType.FindProperty(name)
            ?? throw new InvalidOperationException(
                $"Conflict-target property '{entityType.DisplayName()}.{name}' is not a mapped scalar property."))
            .ToArray();

        var key = entityType.GetKeys().FirstOrDefault(candidate => candidate.Properties.SequenceEqual(properties));
        if (key is not null)
        {
            return new UpsertConflictTarget(key.Properties, key, key.IsPrimaryKey(), IsPhysicallyEnforced: true);
        }

        var index = entityType.GetIndexes()
            .FirstOrDefault(candidate => candidate.IsUnique && candidate.Properties.SequenceEqual(properties));
        if (index is not null)
        {
            return new UpsertConflictTarget(index.Properties, index, UsesPrimaryKey: false, IsPhysicallyEnforced: true);
        }

        if (matchMode == DuckDBUpsertMatchMode.LogicalKeyMerge)
        {
            return new UpsertConflictTarget(
                properties,
                string.Join("\n", conflictPropertyNames),
                UsesPrimaryKey: false,
                IsPhysicallyEnforced: false);
        }

        throw new InvalidOperationException(
            $"The conflict target for '{entityType.DisplayName()}' must be a primary key, alternate key, or unique index. "
            + $"To match on properties without a physical unique constraint, pass {nameof(DuckDBUpsertMatchMode)}.{nameof(DuckDBUpsertMatchMode.LogicalKeyMerge)}.");
    }

    private static DuckDBUpsertPlan BuildPlan(
        IEntityType entityType,
        string table,
        string? schema,
        DuckDBUpsertStrategy strategy,
        bool requiresTargetCardinalityValidation,
        bool usesRemoteValueRendering,
        UpsertConflictTarget conflictTarget)
    {
        var storeObject = StoreObjectIdentifier.Table(table, schema);

        if (entityType.GetStructMetadata() is not null)
        {
            throw new NotSupportedException(
                $"Upsert does not support entity '{entityType.ClrType.Name}' because it contains struct-mapped complex properties. "
                + "STRUCT columns are consolidated at the physical layer and cannot be staged via the DuckDB Appender API. "
                + "Use SaveChanges instead.");
        }

        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"'{entityType.ClrType.Name}' has no primary key; upsert requires a primary key.");
        var conflictProperties = conflictTarget.Properties;

        var keyColumns = GetColumns(primaryKey.Properties, storeObject, entityType, table, "Key");
        var conflictColumns = GetColumns(conflictProperties, storeObject, entityType, table, "Conflict-target");
        var keyColumnSet = keyColumns.ToHashSet(StringComparer.Ordinal);
        var conflictColumnSet = conflictColumns.ToHashSet(StringComparer.Ordinal);
        var insertColumns = new List<string>();
        var updateColumns = new List<string>();
        var writableProperties = new List<IProperty>();

        foreach (var property in entityType.GetProperties())
        {
            var columnName = property.GetColumnName(storeObject);
            if (columnName is null)
            {
                continue;
            }

            if (property.IsShadowProperty())
            {
                throw new NotSupportedException(
                    $"Upsert does not support shadow property '{property.Name}' on '{entityType.ClrType.Name}'. Use SaveChanges instead.");
            }

            if (property.GetComputedColumnSql(storeObject) is not null)
            {
                continue;
            }

            if (!conflictTarget.UsesPrimaryKey
                && !conflictColumnSet.Contains(columnName)
                && IsStoreGeneratedOnAdd(property, storeObject))
            {
                continue;
            }

            insertColumns.Add(columnName);
            writableProperties.Add(property);

            if (!keyColumnSet.Contains(columnName) && !conflictColumnSet.Contains(columnName))
            {
                updateColumns.Add(columnName);
            }
        }

        if (insertColumns.Count == 0)
        {
            throw new InvalidOperationException($"No writable columns were found for table '{table}'.");
        }

        return new DuckDBUpsertPlan(
            table,
            schema,
            strategy,
            insertColumns.ToImmutableArray(),
            updateColumns.ToImmutableArray(),
            conflictColumns,
            requiresTargetCardinalityValidation,
            usesRemoteValueRendering
                ? writableProperties.Select(DuckDBUpsertValueAccessor.Create).ToImmutableArray()
                : [],
            usesRemoteValueRendering
                ? null
                : DuckDBCompiledAppenderRowWriter.Create(entityType.ClrType, writableProperties));
    }

    private static ImmutableArray<string> GetColumns(
        IReadOnlyList<IProperty> properties,
        StoreObjectIdentifier storeObject,
        IEntityType entityType,
        string table,
        string role)
        => properties.Select(property => property.GetColumnName(storeObject)
            ?? throw new InvalidOperationException(
                $"{role} property '{property.Name}' on '{entityType.ClrType.Name}' is not mapped to table '{table}'."))
            .ToImmutableArray();

    private static bool IsStoreGeneratedOnAdd(IProperty property, in StoreObjectIdentifier storeObject)
        => property.ValueGenerated == ValueGenerated.OnAdd
           && (UsesAutoIncrement(property, storeObject)
               || property.TryGetDefaultValue(storeObject, out _)
               || property.GetDefaultValueSql(storeObject) is not null);

    private static bool UsesAutoIncrement(IProperty property, in StoreObjectIdentifier storeObject)
        => property.FindOverrides(storeObject)
                ?.FindAnnotation(DuckDBAnnotationNames.ValueGenerationStrategy)
                ?.Value is DuckDBValueGenerationStrategy.AutoIncrement
           || property.GetValueGenerationStrategy() == DuckDBValueGenerationStrategy.AutoIncrement;

    private readonly record struct UpsertPlanCacheKey(
        string? Schema,
        string Table,
        DuckDBUpsertStrategy Strategy,
        bool RequiresTargetCardinalityValidation,
        bool UsesRemoteValueRendering,
        object ConflictTarget);

    private readonly record struct UpsertConflictTarget(
        IReadOnlyList<IProperty> Properties,
        object MetadataIdentity,
        bool UsesPrimaryKey,
        bool IsPhysicallyEnforced);
}

internal sealed record DuckDBUpsertValueAccessor(
    Func<object, object?> GetProviderValue,
    Microsoft.EntityFrameworkCore.Storage.RelationalTypeMapping TypeMapping)
{
    internal static DuckDBUpsertValueAccessor Create(IProperty property)
    {
        var getter = property.GetGetter();
        var typeMapping = property.GetRelationalTypeMapping();
        var converter = typeMapping.Converter;
        return new DuckDBUpsertValueAccessor(
            entity => converter is null
                ? getter.GetClrValue(entity)
                : converter.ConvertToProvider(getter.GetClrValue(entity)),
            typeMapping);
    }

    internal string GenerateSqlLiteral(object entity)
        => TypeMapping.GenerateProviderValueSqlLiteral(GetProviderValue(entity));
}