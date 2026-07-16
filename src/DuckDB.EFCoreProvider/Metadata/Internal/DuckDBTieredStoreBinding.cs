using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DuckDB.EFCoreProvider.Metadata.Internal;

internal static class DuckDBTieredStoreBinding
{
    public static string CreateRootBindingId(string rootEntityName, string controlKey)
        => CreateId($"root\n{rootEntityName}\n{controlKey}");

    public static string CreateChildBindingId(
        string rootEntityName,
        string controlKey,
        string parentBindingId,
        string parentEntityName,
        string parentNavigationName,
        string childEntityName)
        => CreateId(
            $"child\n{rootEntityName}\n{controlKey}\n{parentBindingId}\n"
            + $"{parentEntityName}\n{parentNavigationName}\n{childEntityName}");

    public static IReadOnlyList<TieredStoreBinding> Deserialize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : JsonSerializer.Deserialize<TieredStoreBinding[]>(value) ?? [];

    public static string Serialize(IEnumerable<TieredStoreBinding> bindings)
        => JsonSerializer.Serialize(bindings.OrderBy(binding => binding.BindingId, StringComparer.Ordinal));

    private static string CreateId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return "tier-" + Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }
}
