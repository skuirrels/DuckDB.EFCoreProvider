using System.Text.Json;
using System.Text.Json.Serialization;

namespace DuckDB.EFCoreProvider.Metadata.Internal;

internal sealed record DuckDBTierPartitionDefinition(
    string PropertyName,
    TierPartitionTransform Transform,
    bool IsImplicit = false);

internal static class DuckDBTierPartitionDefinitionSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(IReadOnlyList<DuckDBTierPartitionDefinition> definitions)
        => JsonSerializer.Serialize(definitions, Options);

    public static IReadOnlyList<DuckDBTierPartitionDefinition> Deserialize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<DuckDBTierPartitionDefinition[]>(value, Options) ?? [];
        }
        catch (JsonException)
        {
            // The first development version stored a comma-separated list of exact-value properties.
            return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(property => new DuckDBTierPartitionDefinition(property, TierPartitionTransform.Value))
                .ToArray();
        }
    }
}
