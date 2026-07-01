using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using System.Text;

namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBUpdateSqlGenerator : UpdateSqlGenerator
{
    public DuckDBUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies) : base(dependencies)
    {
    }

    public override void AppendNextSequenceValueOperation(StringBuilder commandStringBuilder, string name, string? schema)
    {
        commandStringBuilder.Append("SELECT ");
        AppendObtainNextSequenceValueOperation(commandStringBuilder, name, schema);
    }

    /// <inheritdoc />
    public override void AppendObtainNextSequenceValueOperation(StringBuilder commandStringBuilder, string name, string? schema)
    {
        commandStringBuilder.Append("nextval(");
        commandStringBuilder.Append(DuckDBSequenceNameHelper.GenerateSequenceNameLiteral(name, schema));
        commandStringBuilder.Append(')');
    }

    /// <summary>
    ///     Appends a single multi-row <c>INSERT INTO &lt;table&gt; (&lt;cols&gt;) VALUES (..),(..),..</c> statement
    ///     for a run of inserts that all target the same table with the same written and returned columns,
    ///     optionally followed by a <c>RETURNING</c> clause for store-generated values.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Collapsing N single-row inserts into one statement turns N native prepare/execute round-trips
    ///         into one, which is roughly an order of magnitude faster on DuckDB's columnar engine than the
    ///         per-row insert path.
    ///     </para>
    ///     <para>
    ///         Unlike SQL Server (whose <c>OUTPUT</c> ordering is non-deterministic and therefore requires a
    ///         synthetic position column and a <c>MERGE</c>), DuckDB returns <c>RETURNING</c> rows in the same
    ///         order as the supplied <c>VALUES</c> tuples, so generated values are correlated back to each
    ///         command positionally.
    ///     </para>
    /// </remarks>
    /// <param name="commandStringBuilder">The builder to which the SQL is appended.</param>
    /// <param name="modificationCommands">The consecutive insert commands to merge into one statement.</param>
    /// <param name="requiresTransaction">Set to <see langword="true" /> if the appended SQL must run inside a transaction.</param>
    /// <returns>
    ///     <see cref="ResultSetMapping.NotLastInResultSet" /> when a <c>RETURNING</c> clause is emitted (the
    ///     caller promotes the final command's mapping to <see cref="ResultSetMapping.LastInResultSet" />);
    ///     otherwise <see cref="ResultSetMapping.NoResults" />.
    /// </returns>
    public virtual ResultSetMapping AppendBulkInsertOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyList<IReadOnlyModificationCommand> modificationCommands,
        out bool requiresTransaction)
    {
        var firstCommand = modificationCommands[0];
        var table = firstCommand.TableName;
        var schema = firstCommand.Schema;

        var writeOperations = firstCommand.ColumnModifications.Where(o => o.IsWrite).ToList();
        var readOperations = firstCommand.ColumnModifications.Where(o => o.IsRead).ToList();

        AppendInsertCommandHeader(commandStringBuilder, table, schema, writeOperations);
        AppendValuesHeader(commandStringBuilder, writeOperations);
        AppendValues(commandStringBuilder, table, schema, writeOperations);

        for (var i = 1; i < modificationCommands.Count; i++)
        {
            commandStringBuilder.AppendLine(",");
            AppendValues(
                commandStringBuilder,
                table,
                schema,
                modificationCommands[i].ColumnModifications.Where(o => o.IsWrite).ToList());
        }

        // Inserts run inside the change-tracking transaction; no additional transaction is required here.
        requiresTransaction = false;

        if (readOperations.Count > 0)
        {
            AppendReturningClause(commandStringBuilder, readOperations);
            commandStringBuilder.AppendLine(SqlGenerationHelper.StatementTerminator);

            // One result row per command, consumed positionally; the caller promotes the final command's
            // mapping to LastInResultSet.
            return ResultSetMapping.NotLastInResultSet;
        }

        commandStringBuilder.AppendLine(SqlGenerationHelper.StatementTerminator);

        return ResultSetMapping.NoResults;
    }

    /// <summary>
    ///     Appends a single <c>UPDATE &lt;table&gt; SET &lt;cols&gt; = v.&lt;cols&gt; FROM (VALUES (..),(..)) AS v(..)
    ///     WHERE &lt;key&gt; = v.&lt;key&gt;</c> statement for a run of updates that all target the same table with the
    ///     same written columns and the same (key-only) condition columns.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each <c>VALUES</c> tuple carries the row's original key value(s) followed by its new column
    ///         values; the join applies every row's new values in one statement. This is roughly an order of
    ///         magnitude faster than issuing one <c>UPDATE</c> per row on DuckDB.
    ///     </para>
    ///     <para>
    ///         The caller only routes updates here when their condition columns are the primary key (no
    ///         concurrency tokens) and they read no values back, so no <c>RETURNING</c> clause or result-set
    ///         consumption is required.
    ///     </para>
    /// </remarks>
    /// <param name="commandStringBuilder">The builder to which the SQL is appended.</param>
    /// <param name="modificationCommands">The consecutive update commands to merge into one statement.</param>
    /// <param name="requiresTransaction">Set to <see langword="true" /> if the appended SQL must run inside a transaction.</param>
    /// <returns><see cref="ResultSetMapping.NoResults" />.</returns>
    public virtual ResultSetMapping AppendBulkUpdateOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyList<IReadOnlyModificationCommand> modificationCommands,
        out bool requiresTransaction)
    {
        var helper = SqlGenerationHelper;
        var firstCommand = modificationCommands[0];
        var table = firstCommand.TableName;
        var schema = firstCommand.Schema;

        var writeOperations = firstCommand.ColumnModifications.Where(o => o.IsWrite).ToList();
        var keyOperations = firstCommand.ColumnModifications.Where(o => o.IsCondition).ToList();

        // UPDATE <table> SET <col> = v.<col>, ...
        commandStringBuilder.Append("UPDATE ");
        commandStringBuilder.Append(helper.DelimitIdentifier(table, schema));
        commandStringBuilder.Append(" SET ");
        for (var i = 0; i < writeOperations.Count; i++)
        {
            if (i > 0)
            {
                commandStringBuilder.Append(", ");
            }

            var column = helper.DelimitIdentifier(writeOperations[i].ColumnName);
            commandStringBuilder.Append(column).Append(" = v.").Append(column);
        }

        // FROM (VALUES (key.., write..), ...) AS v(keyCols.., writeCols..)
        commandStringBuilder.AppendLine();
        commandStringBuilder.Append("FROM (VALUES ");
        for (var c = 0; c < modificationCommands.Count; c++)
        {
            if (c > 0)
            {
                commandStringBuilder.Append(", ");
            }

            AppendBulkUpdateValuesTuple(commandStringBuilder, modificationCommands[c], helper);
        }

        commandStringBuilder.Append(") AS v(");
        var firstColumn = true;
        foreach (var operation in keyOperations.Concat(writeOperations))
        {
            if (!firstColumn)
            {
                commandStringBuilder.Append(", ");
            }

            commandStringBuilder.Append(helper.DelimitIdentifier(operation.ColumnName));
            firstColumn = false;
        }

        commandStringBuilder.Append(')');

        // WHERE <table>.<key> = v.<key> AND ...
        commandStringBuilder.AppendLine();
        commandStringBuilder.Append("WHERE ");
        for (var i = 0; i < keyOperations.Count; i++)
        {
            if (i > 0)
            {
                commandStringBuilder.Append(" AND ");
            }

            // Qualify with the (unschemed) table name so the reference resolves to the UPDATE target.
            var column = helper.DelimitIdentifier(keyOperations[i].ColumnName);
            commandStringBuilder
                .Append(helper.DelimitIdentifier(table))
                .Append('.')
                .Append(column)
                .Append(" = v.")
                .Append(column);
        }

        commandStringBuilder.AppendLine(helper.StatementTerminator);

        requiresTransaction = false;

        return ResultSetMapping.NoResults;
    }

    private void AppendBulkUpdateValuesTuple(
        StringBuilder commandStringBuilder,
        IReadOnlyModificationCommand command,
        ISqlGenerationHelper helper)
    {
        commandStringBuilder.Append('(');

        var first = true;

        // Key columns first (original values, matching the alias column order), then written values.
        foreach (var operation in command.ColumnModifications.Where(o => o.IsCondition))
        {
            if (!first)
            {
                commandStringBuilder.Append(", ");
            }

            commandStringBuilder.Append(helper.GenerateParameterNamePlaceholder(operation.OriginalParameterName!));
            first = false;
        }

        foreach (var operation in command.ColumnModifications.Where(o => o.IsWrite))
        {
            if (!first)
            {
                commandStringBuilder.Append(", ");
            }

            commandStringBuilder.Append(helper.GenerateParameterNamePlaceholder(operation.ParameterName!));
            first = false;
        }

        commandStringBuilder.Append(')');
    }

    /// <summary>
    ///     Appends a single statement that deletes a run of rows that all target the same table with the same
    ///     (key-only) condition columns. For a single-column key this emits
    ///     <c>DELETE FROM &lt;table&gt; WHERE &lt;key&gt; IN (..)</c>; for a composite key it emits
    ///     <c>DELETE FROM &lt;table&gt; USING (VALUES (..),(..)) AS v(..) WHERE &lt;table&gt;.&lt;key&gt; = v.&lt;key&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     Collapsing N single-row deletes into one statement is roughly an order of magnitude (and up to ~20×)
    ///     faster than the per-row delete path on DuckDB. The caller only routes deletes here when their
    ///     condition columns are the primary key (no concurrency tokens), so no row-count verification or
    ///     result-set consumption is required.
    /// </remarks>
    /// <param name="commandStringBuilder">The builder to which the SQL is appended.</param>
    /// <param name="modificationCommands">The consecutive delete commands to merge into one statement.</param>
    /// <param name="requiresTransaction">Set to <see langword="true" /> if the appended SQL must run inside a transaction.</param>
    /// <returns><see cref="ResultSetMapping.NoResults" />.</returns>
    public virtual ResultSetMapping AppendBulkDeleteOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyList<IReadOnlyModificationCommand> modificationCommands,
        out bool requiresTransaction)
    {
        var helper = SqlGenerationHelper;
        var firstCommand = modificationCommands[0];
        var table = firstCommand.TableName;
        var schema = firstCommand.Schema;
        var keyOperations = firstCommand.ColumnModifications.Where(o => o.IsCondition).ToList();

        commandStringBuilder.Append("DELETE FROM ");
        commandStringBuilder.Append(helper.DelimitIdentifier(table, schema));

        if (keyOperations.Count == 1)
        {
            // Single-column key: DELETE FROM t WHERE <key> IN ($k0, $k1, ...)
            commandStringBuilder.Append(" WHERE ");
            commandStringBuilder.Append(helper.DelimitIdentifier(keyOperations[0].ColumnName));
            commandStringBuilder.Append(" IN (");

            for (var c = 0; c < modificationCommands.Count; c++)
            {
                if (c > 0)
                {
                    commandStringBuilder.Append(", ");
                }

                var key = modificationCommands[c].ColumnModifications.First(o => o.IsCondition);
                commandStringBuilder.Append(helper.GenerateParameterNamePlaceholder(key.OriginalParameterName!));
            }

            commandStringBuilder.Append(')');
        }
        else
        {
            // Composite key: DELETE FROM t USING (VALUES (..),(..)) AS v(k1,k2) WHERE t.k1=v.k1 AND t.k2=v.k2
            commandStringBuilder.Append(" USING (VALUES ");
            for (var c = 0; c < modificationCommands.Count; c++)
            {
                if (c > 0)
                {
                    commandStringBuilder.Append(", ");
                }

                commandStringBuilder.Append('(');
                var keys = modificationCommands[c].ColumnModifications.Where(o => o.IsCondition).ToList();
                for (var i = 0; i < keys.Count; i++)
                {
                    if (i > 0)
                    {
                        commandStringBuilder.Append(", ");
                    }

                    commandStringBuilder.Append(helper.GenerateParameterNamePlaceholder(keys[i].OriginalParameterName!));
                }

                commandStringBuilder.Append(')');
            }

            commandStringBuilder.Append(") AS v(");
            for (var i = 0; i < keyOperations.Count; i++)
            {
                if (i > 0)
                {
                    commandStringBuilder.Append(", ");
                }

                commandStringBuilder.Append(helper.DelimitIdentifier(keyOperations[i].ColumnName));
            }

            commandStringBuilder.Append(") WHERE ");
            for (var i = 0; i < keyOperations.Count; i++)
            {
                if (i > 0)
                {
                    commandStringBuilder.Append(" AND ");
                }

                var column = helper.DelimitIdentifier(keyOperations[i].ColumnName);
                commandStringBuilder
                    .Append(helper.DelimitIdentifier(table))
                    .Append('.')
                    .Append(column)
                    .Append(" = v.")
                    .Append(column);
            }
        }

        commandStringBuilder.AppendLine(helper.StatementTerminator);

        requiresTransaction = false;

        return ResultSetMapping.NoResults;
    }
}
