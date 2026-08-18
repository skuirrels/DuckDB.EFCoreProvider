using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text;

namespace DuckDB.EFCoreProvider.Extensions.Internal;

internal sealed class DuckDBUpsertSqlRenderer(ISqlGenerationHelper sqlGenerationHelper)
{
    private const string CardinalityError =
        "The upsert conflict target matched multiple existing rows; logical key uniqueness is required.";

    internal string RenderCreateTemporaryTable(DuckDBUpsertPlan plan, string temporaryTable)
        => RenderCreateStagingTable(plan, temporaryTable, temporary: true);

    internal string RenderCreateStagingTable(
        DuckDBUpsertPlan plan,
        string stagingTable,
        bool temporary,
        string? stagingSchema = null)
    {
        var insertColumnList = RenderColumnList(plan.InsertColumns);
        var stagingTarget = temporary ? Delimit(stagingTable) : Delimit(stagingTable, stagingSchema);
        return $"CREATE {(temporary ? "TEMPORARY " : string.Empty)}TABLE {stagingTarget} AS SELECT {insertColumnList} "
               + $"FROM {Delimit(plan.Table, plan.Schema)} WHERE false;";
    }

    internal string RenderUpsertFromTemporaryTable(
        DuckDBUpsertPlan plan,
        string temporaryTable,
        string? temporarySchema = null)
        => plan.Strategy switch
        {
            DuckDBUpsertStrategy.InsertOnConflict => RenderInsertOnConflict(plan, temporaryTable, temporarySchema),
            DuckDBUpsertStrategy.Merge => RenderMerge(plan, temporaryTable, temporarySchema),
            _ => throw new NotSupportedException($"DuckDB upsert strategy '{plan.Strategy}' is not supported."),
        };

    internal string RenderUpsertBatch(
        DuckDBUpsertPlan plan,
        string temporaryTable,
        string? temporarySchema = null)
        => RenderUpsertFromTemporaryTable(plan, temporaryTable, temporarySchema)
           + $" DELETE FROM {Delimit(temporaryTable, temporarySchema)};";

    internal string RenderDropTemporaryTable(string temporaryTable, string? temporarySchema = null)
        => $"DROP TABLE IF EXISTS {Delimit(temporaryTable, temporarySchema)};";

    internal string RenderInsertValues<TEntity>(
        DuckDBUpsertPlan plan,
        string stagingTable,
        string? stagingSchema,
        IReadOnlyList<TEntity> entities)
        where TEntity : class
    {
        if (entities.Count == 0)
        {
            throw new ArgumentException("At least one entity is required.", nameof(entities));
        }

        if (plan.ValueAccessors.Length != plan.InsertColumns.Length)
        {
            throw new InvalidOperationException("The configured upsert plan does not contain remote value accessors.");
        }

        var sql = new StringBuilder()
            .Append("INSERT INTO ")
            .Append(Delimit(stagingTable, stagingSchema))
            .Append(" (")
            .Append(RenderColumnList(plan.InsertColumns))
            .Append(") VALUES ");
        for (var rowIndex = 0; rowIndex < entities.Count; rowIndex++)
        {
            if (rowIndex > 0)
            {
                sql.Append(", ");
            }

            sql.Append('(');
            for (var columnIndex = 0; columnIndex < plan.ValueAccessors.Length; columnIndex++)
            {
                if (columnIndex > 0)
                {
                    sql.Append(", ");
                }

                sql.Append(plan.ValueAccessors[columnIndex].GenerateSqlLiteral(entities[rowIndex]));
            }

            sql.Append(')');
        }

        return sql.Append(';').ToString();
    }

    private string RenderInsertOnConflict(
        DuckDBUpsertPlan plan,
        string temporaryTable,
        string? temporarySchema)
    {
        var insertColumnList = RenderColumnList(plan.InsertColumns);
        var conflictSuffix = new StringBuilder()
            .Append(" ON CONFLICT (")
            .AppendJoin(", ", plan.ConflictColumns.Select(Delimit))
            .Append(')')
            .Append(plan.UpdateColumns.Length == 0
                ? " DO NOTHING"
                : " DO UPDATE SET " + string.Join(
                    ", ",
                    plan.UpdateColumns.Select(column => $"{Delimit(column)} = excluded.{Delimit(column)}")))
            .ToString();

        return $"INSERT INTO {Delimit(plan.Table, plan.Schema)} ({insertColumnList}) "
               + $"SELECT {insertColumnList} FROM {RenderStagedSource(plan, temporaryTable, temporarySchema)}{conflictSuffix};";
    }

    // ON CONFLICT DO UPDATE and MERGE apply only one staged row per conflict key, and which duplicate
    // they pick is engine-defined. Keeping only the greatest rowid per key — appended rows keep input
    // order — pins the outcome to the last occurrence, matching what sequential per-row upserts produce.
    // Key-only DO NOTHING shapes keep the first inserted row per key either way and skip the window scan.
    private string RenderStagedSource(
        DuckDBUpsertPlan plan,
        string temporaryTable,
        string? temporarySchema)
    {
        if (plan.UpdateColumns.Length == 0)
        {
            return Delimit(temporaryTable, temporarySchema);
        }

        var insertColumnList = RenderColumnList(plan.InsertColumns);
        var rowNumber = Delimit("__duckdb_upsert_row_number");
        return $"(SELECT {insertColumnList} FROM (SELECT {insertColumnList}, "
               + $"row_number() OVER (PARTITION BY {RenderColumnList(plan.ConflictColumns)} ORDER BY rowid DESC) AS {rowNumber} "
               + $"FROM {Delimit(temporaryTable, temporarySchema)}) AS {Delimit("__duckdb_upsert_ranked")} "
               + $"WHERE {rowNumber} = 1)";
    }

    private string RenderMerge(DuckDBUpsertPlan plan, string temporaryTable, string? temporarySchema)
    {
        var targetAlias = Delimit("target");
        var sourceAlias = Delimit("source");
        var insertColumnList = RenderColumnList(plan.InsertColumns);
        var stagedSource = RenderStagedSource(plan, temporaryTable, temporarySchema);
        var source = plan.RequiresTargetCardinalityValidation
            ? RenderCardinalityValidatedSource(plan, stagedSource)
            : stagedSource;
        var matchPredicates = plan.ConflictColumns.Select(
            column => $"{Qualify(targetAlias, column)} = {Qualify(sourceAlias, column)}");
        var updateAssignments = plan.UpdateColumns.Select(
            column => $"{Delimit(column)} = {Qualify(sourceAlias, column)}");
        var sourceValues = plan.InsertColumns.Select(column => Qualify(sourceAlias, column));

        var sql = new StringBuilder()
            .Append("MERGE INTO ")
            .Append(Delimit(plan.Table, plan.Schema))
            .Append(" AS ")
            .Append(targetAlias)
            .Append(" USING ")
            .Append(source)
            .Append(" AS ")
            .Append(sourceAlias)
            .Append(" ON ")
            .AppendJoin(" AND ", matchPredicates);

        if (plan.UpdateColumns.Length > 0)
        {
            sql.Append(" WHEN MATCHED THEN UPDATE SET ").AppendJoin(", ", updateAssignments);
        }

        return sql
            .Append(" WHEN NOT MATCHED THEN INSERT (")
            .Append(insertColumnList)
            .Append(") VALUES (")
            .AppendJoin(", ", sourceValues)
            .Append(");")
            .ToString();
    }

    private string RenderCardinalityValidatedSource(DuckDBUpsertPlan plan, string stagedSource)
    {
        var incomingAlias = Delimit("__duckdb_upsert_incoming");
        var existingAlias = Delimit("__duckdb_upsert_existing");
        var matches = plan.ConflictColumns.Select(
            column => $"{Qualify(existingAlias, column)} = {Qualify(incomingAlias, column)}");

        return new StringBuilder()
            .Append("(SELECT ")
            .Append(incomingAlias)
            .Append(".* FROM ")
            .Append(stagedSource)
            .Append(" AS ")
            .Append(incomingAlias)
            .Append(" WHERE CASE WHEN (SELECT count(*) FROM ")
            .Append(Delimit(plan.Table, plan.Schema))
            .Append(" AS ")
            .Append(existingAlias)
            .Append(" WHERE ")
            .AppendJoin(" AND ", matches)
            .Append(") > 1 THEN error('")
            .Append(CardinalityError)
            .Append("') ELSE true END)")
            .ToString();
    }

    private string RenderColumnList(IReadOnlyList<string> columns)
        => string.Join(", ", columns.Select(Delimit));

    private string Qualify(string delimitedAlias, string column)
        => $"{delimitedAlias}.{Delimit(column)}";

    private string Delimit(string identifier)
        => sqlGenerationHelper.DelimitIdentifier(identifier);

    private string Delimit(string identifier, string? schema)
        => sqlGenerationHelper.DelimitIdentifier(identifier, schema);
}