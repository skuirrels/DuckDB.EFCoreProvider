using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DuckDB.EFCoreProvider.Metadata.Internal;

internal readonly record struct DuckDBFileSourceFunction(string Name, string? Schema, bool IsBuiltIn)
{
    public static DuckDBFileSourceFunction Parse(string function)
        => function switch
        {
            "read_parquet" or "read_csv" or "read_json" => new(function, Schema: null, IsBuiltIn: true),
            _ => ParseCustom(function)
        };

    private static DuckDBFileSourceFunction ParseCustom(string function)
    {
        var separator = function.IndexOf('.');
        if (separator < 0)
        {
            return new DuckDBFileSourceFunction(function, Schema: null, IsBuiltIn: false);
        }

        if (separator == 0
            || separator == function.Length - 1
            || function.IndexOf('.', separator + 1) >= 0)
        {
            throw new InvalidOperationException(
                $"Custom DuckDB file-source function '{function}' must be an identifier or a schema-qualified identifier.");
        }

        return new DuckDBFileSourceFunction(
            function[(separator + 1)..],
            function[..separator],
            IsBuiltIn: false);
    }
}

internal readonly record struct DuckDBFileSourceDefinition(DuckDBFileSourceFunction Function, string Path)
{
    public static bool TryCreate(IEntityType entityType, out DuckDBFileSourceDefinition definition)
    {
        var function = entityType.GetDuckDBFileSourceFunction();
        var path = entityType.GetDuckDBFileSourcePath();

        if (function is null && path is null)
        {
            definition = default;
            return false;
        }

        if (string.IsNullOrWhiteSpace(function) || string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                $"Entity '{entityType.DisplayName()}' has incomplete DuckDB file-source metadata. "
                + "Both a file-reading function and a path are required.");
        }

        definition = new DuckDBFileSourceDefinition(DuckDBFileSourceFunction.Parse(function), path);
        return true;
    }

    public static bool TryCreate(ITableBase table, out DuckDBFileSourceDefinition definition)
        => TryCreate(
            table.EntityTypeMappings
                .Select(mapping => mapping.TypeBase)
                .OfType<IEntityType>()
                .Distinct(),
            table.Name,
            out definition);

    public static bool TryCreate(
        IEnumerable<IEntityType> entityTypes,
        string tableName,
        out DuckDBFileSourceDefinition definition)
    {
        DuckDBFileSourceDefinition? source = null;
        IEntityType? sourceEntityType = null;
        IEntityType? ordinaryEntityType = null;

        foreach (var entityType in entityTypes)
        {
            if (!TryCreate(entityType, out var candidate))
            {
                ordinaryEntityType ??= entityType;
                continue;
            }

            if (source is { } existing && existing != candidate)
            {
                throw new InvalidOperationException(
                    $"Table '{tableName}' contains entity mappings with conflicting DuckDB file sources: "
                    + $"'{sourceEntityType!.DisplayName()}' and '{entityType.DisplayName()}'.");
            }

            source = candidate;
            sourceEntityType ??= entityType;
        }

        if (source is not null && ordinaryEntityType is not null)
        {
            throw new InvalidOperationException(
                $"Table '{tableName}' mixes the file-backed entity mapping '{sourceEntityType!.DisplayName()}' "
                + $"with the ordinary entity mapping '{ordinaryEntityType.DisplayName()}'. All entity mappings on a shared table "
                + "must use the same DuckDB file source, or none of them may use one.");
        }

        definition = source.GetValueOrDefault();
        return source is not null;
    }
}