using DuckDB.EFCoreProvider.Infrastructure.Internal;
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
    private readonly bool _isDuckLake;

    public DuckDBUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies)
        : this(dependencies, null)
    {
    }

    public DuckDBUpdateSqlGenerator(
        UpdateSqlGeneratorDependencies dependencies,
        IDuckLakeSingletonOptions? singletonOptions)
        : base(dependencies)
    {
        _isDuckLake = singletonOptions?.IsDuckLake == true;
    }

    /// <inheritdoc />
    public override ResultSetMapping AppendInsertOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyModificationCommand command,
        int commandPosition,
        out bool requiresTransaction)
    {
        if (!_isDuckLake)
        {
            return base.AppendInsertOperation(commandStringBuilder, command, commandPosition, out requiresTransaction);
        }

        var operations = command.ColumnModifications;
        var readOperations = operations.Where(operation => operation.IsRead).ToList();
        EnsureNoStoreGeneratedValues(command, readOperations);

        var writeOperations = operations.Where(operation => operation.IsWrite).ToList();
        AppendInsertCommand(commandStringBuilder, command.TableName, command.Schema, writeOperations, []);
        requiresTransaction = false;
        return ResultSetMapping.NoResults;
    }

    /// <inheritdoc />
    public override ResultSetMapping AppendUpdateOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyModificationCommand command,
        int commandPosition,
        out bool requiresTransaction)
    {
        if (!_isDuckLake)
        {
            return base.AppendUpdateOperation(commandStringBuilder, command, commandPosition, out requiresTransaction);
        }

        var operations = command.ColumnModifications;
        var readOperations = operations.Where(operation => operation.IsRead).ToList();
        EnsureNoStoreGeneratedValues(command, readOperations);

        AppendUpdateCommand(
            commandStringBuilder,
            command.TableName,
            command.Schema,
            operations.Where(operation => operation.IsWrite).ToList(),
            [],
            operations.Where(operation => operation.IsCondition).ToList());
        // DuckLake does not physically enforce EF's logical keys. If duplicate key rows exist, an update can
        // affect more than one row and the modification batch detects that only after execution. Require a
        // transaction so EF can roll the statement back before surfacing DbUpdateConcurrencyException.
        requiresTransaction = true;
        return ResultSetMapping.NoResults;
    }

    /// <inheritdoc />
    public override ResultSetMapping AppendDeleteOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyModificationCommand command,
        int commandPosition,
        out bool requiresTransaction)
    {
        if (!_isDuckLake)
        {
            return base.AppendDeleteOperation(commandStringBuilder, command, commandPosition, out requiresTransaction);
        }

        AppendDeleteCommand(
            commandStringBuilder,
            command.TableName,
            command.Schema,
            [],
            command.ColumnModifications.Where(operation => operation.IsCondition).ToList());
        // As with updates, the affected-row check happens after execution and must be able to roll back a
        // multi-row match caused by duplicate logical keys.
        requiresTransaction = true;
        return ResultSetMapping.NoResults;
    }

    private static void EnsureNoStoreGeneratedValues(
        IReadOnlyModificationCommand command,
        IReadOnlyList<IColumnModification> readOperations)
    {
        if (readOperations.Count > 0)
        {
            throw new NotSupportedException(
                $"DuckLake does not support INSERT/UPDATE RETURNING. Table '{command.TableName}' has "
                + $"store-generated column(s): {string.Join(", ", readOperations.Select(operation => operation.ColumnName))}. "
                + "Configure client-assigned values or literal defaults that do not need to be read back.");
        }
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

        var writeIndexes = GetColumnModificationIndexes(firstCommand, o => o.IsWrite);
        var writeOperations = GetColumnModifications(firstCommand, writeIndexes);
        var readOperations = GetColumnModifications(firstCommand, o => o.IsRead);

        AppendInsertCommandHeader(commandStringBuilder, table, schema, writeOperations);
        AppendValuesHeader(commandStringBuilder, writeOperations);
        AppendValues(commandStringBuilder, table, schema, writeOperations);

        var reusableWriteOperations = new List<IColumnModification>(writeOperations.Count);
        for (var i = 1; i < modificationCommands.Count; i++)
        {
            commandStringBuilder.AppendLine(",");
            CollectColumnModifications(modificationCommands[i], writeIndexes, reusableWriteOperations);
            AppendValues(
                commandStringBuilder,
                table,
                schema,
                reusableWriteOperations);
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

        var writeIndexes = GetColumnModificationIndexes(firstCommand, o => o.IsWrite);
        var keyIndexes = GetColumnModificationIndexes(firstCommand, o => o.IsCondition);
        var writeOperations = GetColumnModifications(firstCommand, writeIndexes);
        var keyOperations = GetColumnModifications(firstCommand, keyIndexes);

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

            AppendBulkUpdateValuesTuple(commandStringBuilder, modificationCommands[c], keyIndexes, writeIndexes, helper);
        }

        commandStringBuilder.Append(") AS v(");
        var firstColumn = true;
        for (var i = 0; i < keyOperations.Count; i++)
        {
            if (!firstColumn)
            {
                commandStringBuilder.Append(", ");
            }

            commandStringBuilder.Append(helper.DelimitIdentifier(keyOperations[i].ColumnName));
            firstColumn = false;
        }

        for (var i = 0; i < writeOperations.Count; i++)
        {
            if (!firstColumn)
            {
                commandStringBuilder.Append(", ");
            }

            commandStringBuilder.Append(helper.DelimitIdentifier(writeOperations[i].ColumnName));
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
        IReadOnlyList<int> keyIndexes,
        IReadOnlyList<int> writeIndexes,
        ISqlGenerationHelper helper)
    {
        commandStringBuilder.Append('(');

        var first = true;

        // Key columns first (original values, matching the alias column order), then written values.
        for (var i = 0; i < keyIndexes.Count; i++)
        {
            if (!first)
            {
                commandStringBuilder.Append(", ");
            }

            var operation = command.ColumnModifications[keyIndexes[i]];
            commandStringBuilder.Append(helper.GenerateParameterNamePlaceholder(operation.OriginalParameterName!));
            first = false;
        }

        for (var i = 0; i < writeIndexes.Count; i++)
        {
            if (!first)
            {
                commandStringBuilder.Append(", ");
            }

            var operation = command.ColumnModifications[writeIndexes[i]];
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
        var keyIndexes = GetColumnModificationIndexes(firstCommand, o => o.IsCondition);
        var keyOperations = GetColumnModifications(firstCommand, keyIndexes);

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

                var key = modificationCommands[c].ColumnModifications[keyIndexes[0]];
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
                var columnModifications = modificationCommands[c].ColumnModifications;
                for (var i = 0; i < keyIndexes.Length; i++)
                {
                    if (i > 0)
                    {
                        commandStringBuilder.Append(", ");
                    }

                    commandStringBuilder.Append(helper.GenerateParameterNamePlaceholder(columnModifications[keyIndexes[i]].OriginalParameterName!));
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

    private static int[] GetColumnModificationIndexes(
        IReadOnlyModificationCommand command,
        Func<IColumnModification, bool> predicate)
    {
        var indexes = new List<int>();
        var columnModifications = command.ColumnModifications;
        for (var i = 0; i < columnModifications.Count; i++)
        {
            if (predicate(columnModifications[i]))
            {
                indexes.Add(i);
            }
        }

        return indexes.ToArray();
    }

    private static List<IColumnModification> GetColumnModifications(
        IReadOnlyModificationCommand command,
        Func<IColumnModification, bool> predicate)
    {
        var operations = new List<IColumnModification>();
        var columnModifications = command.ColumnModifications;
        for (var i = 0; i < columnModifications.Count; i++)
        {
            var operation = columnModifications[i];
            if (predicate(operation))
            {
                operations.Add(operation);
            }
        }

        return operations;
    }

    private static List<IColumnModification> GetColumnModifications(
        IReadOnlyModificationCommand command,
        IReadOnlyList<int> indexes)
    {
        var operations = new List<IColumnModification>(indexes.Count);
        CollectColumnModifications(command, indexes, operations);
        return operations;
    }

    private static void CollectColumnModifications(
        IReadOnlyModificationCommand command,
        IReadOnlyList<int> indexes,
        List<IColumnModification> target)
    {
        target.Clear();
        var columnModifications = command.ColumnModifications;
        for (var i = 0; i < indexes.Count; i++)
        {
            target.Add(columnModifications[indexes[i]]);
        }
    }
}
