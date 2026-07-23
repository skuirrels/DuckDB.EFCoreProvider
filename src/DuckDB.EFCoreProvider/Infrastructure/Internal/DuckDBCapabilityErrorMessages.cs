namespace DuckDB.EFCoreProvider.Infrastructure.Internal;

internal static class DuckDBCapabilityErrorMessages
{
    public const string MigrationsNotSupported =
        "EF Core migrations are not supported by the configured DuckDB engine capabilities. The active backend "
        + "does not provide the schema constraints and/or locking semantics required by EF's migrations history "
        + "contract. Use Database.EnsureCreated() for a new catalog, and apply reviewed schema-evolution SQL explicitly.";

    public const string SequencesNotSupported =
        "The configured DuckDB engine does not support sequences. Configure client-assigned keys or a client-side "
        + "value generator.";

    public const string SaveChangesBatchingNotSupported =
        "SaveChanges batching is not supported by the configured DuckDB engine capabilities. Use standard SaveChanges, "
        + "the appender-based BulkInsert API, or the capability-selected Upsert API instead.";

    public static string TieredStorageNotSupported(string entity)
        => $"Entity '{entity}' enables provider tiered storage, which is not supported by the configured DuckDB "
            + "engine capabilities.";

    public static string AutoIncrementNotSupported(string property)
        => $"The configured DuckDB engine does not support auto-increment or sequence-backed values. Property "
            + $"'{property}' must use a client-assigned value.";

    public static string GeneratedColumnNotSupported(string property)
        => $"The configured DuckDB engine does not support generated columns. Property '{property}' must be computed "
            + "by the application.";

    public static string SqlDefaultExpressionNotSupported(string property)
        => $"The configured DuckDB engine supports only literal defaults. Property '{property}' uses a SQL default "
            + "expression; assign the value in the application instead. A literal HasDefaultValue(...) may be retained "
            + "only with ValueGeneratedNever().";

    public static string LiteralDefaultCannotBeRead(string property)
        => $"The configured DuckDB engine can store a literal default for '{property}', but EF cannot read that "
            + "generated value back because the engine does not support RETURNING. Configure ValueGeneratedNever() "
            + "and assign the value in tracked writes, or remove the default.";

    public static string StoreGeneratedValueCannotBeRead(string property)
        => $"The configured DuckDB engine cannot read store-generated values for '{property}'. Compute and assign the "
            + "value in the application instead.";

    public static string MigrationColumnNotSupported(string table, string column)
        => $"The configured DuckDB engine does not support generated columns or SQL default expressions. Column "
            + $"'{table}.{column}' must use an application-assigned value or a literal default.";

    public static string StoreGeneratedColumnsCannotBeRead(
        string table,
        IEnumerable<string> columns)
        => $"The configured DuckDB engine does not support INSERT/UPDATE RETURNING. Table '{table}' has store-generated "
            + $"column(s): {string.Join(", ", columns)}. Configure client-assigned values or literal defaults that do "
            + "not need to be read back.";
}
