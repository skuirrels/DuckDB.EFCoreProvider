using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Internal;
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
    private readonly IDuckDBEngineCapabilities _capabilities;

    public DuckDBUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies)
        : this(dependencies, null, null)
    {
    }

    public DuckDBUpdateSqlGenerator(
        UpdateSqlGeneratorDependencies dependencies,
        IDuckLakeSingletonOptions? singletonOptions)
        : this(dependencies, singletonOptions, null)
    {
    }

    public DuckDBUpdateSqlGenerator(
        UpdateSqlGeneratorDependencies dependencies,
        IDuckLakeSingletonOptions? singletonOptions,
        IDuckDBEngineCapabilities? capabilities)
        : base(dependencies)
    {
        _capabilities = capabilities
            ?? new DuckDBEngineCapabilities(singletonOptions?.IsDuckLake == true);
    }

    /// <inheritdoc />
    public override ResultSetMapping AppendInsertOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyModificationCommand command,
        int commandPosition,
        out bool requiresTransaction)
    {
        if (_capabilities.SupportsReturning)
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
        if (_capabilities.SupportsReturning)
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
        if (_capabilities.SupportsReturning)
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
        => AppendBulkInsertOperation(
            commandStringBuilder,
            DuckDBBulkInsertPlanner.Create(modificationCommands),
            out requiresTransaction);

    internal ResultSetMapping AppendBulkInsertOperation(
        StringBuilder commandStringBuilder,
        DuckDBBulkInsertPlan plan,
        out bool requiresTransaction)
    {
        var writeOperations = new List<IColumnModification>(plan.WriteColumnCount);
        var readOperations = new List<IColumnModification>(plan.ReadColumnCount);
        plan.CollectWriteColumns(0, writeOperations);
        plan.CollectReadColumns(readOperations);

        AppendInsertCommandHeader(commandStringBuilder, plan.TableName, plan.Schema, writeOperations);
        AppendValuesHeader(commandStringBuilder, writeOperations);
        AppendValues(commandStringBuilder, plan.TableName, plan.Schema, writeOperations);

        for (var rowIndex = 1; rowIndex < plan.RowCount; rowIndex++)
        {
            commandStringBuilder.AppendLine(",");
            plan.CollectWriteColumns(rowIndex, writeOperations);
            AppendValues(
                commandStringBuilder,
                plan.TableName,
                plan.Schema,
                writeOperations);
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
        => AppendBulkUpdateOperation(
            commandStringBuilder,
            DuckDBBulkUpdatePlanner.Create(modificationCommands),
            out requiresTransaction);

    internal ResultSetMapping AppendBulkUpdateOperation(
        StringBuilder commandStringBuilder,
        DuckDBBulkUpdatePlan plan,
        out bool requiresTransaction)
    {
        var helper = SqlGenerationHelper;

        // UPDATE <table> SET <col> = v.<col>, ...
        commandStringBuilder.Append("UPDATE ");
        commandStringBuilder.Append(helper.DelimitIdentifier(plan.TableName, plan.Schema));
        commandStringBuilder.Append(" SET ");
        for (var i = 0; i < plan.WriteColumnCount; i++)
        {
            if (i > 0)
            {
                commandStringBuilder.Append(", ");
            }

            var column = helper.DelimitIdentifier(plan.GetWriteColumnName(i));
            commandStringBuilder.Append(column).Append(" = v.").Append(column);
        }

        // FROM (VALUES (key.., write..), ...) AS v(keyCols.., writeCols..)
        commandStringBuilder.AppendLine();
        commandStringBuilder.Append("FROM (VALUES ");
        for (var rowIndex = 0; rowIndex < plan.RowCount; rowIndex++)
        {
            if (rowIndex > 0)
            {
                commandStringBuilder.Append(", ");
            }

            AppendBulkUpdateValuesTuple(commandStringBuilder, plan, rowIndex, helper);
        }

        commandStringBuilder.Append(") AS v(");
        var firstColumn = true;
        for (var i = 0; i < plan.KeyColumnCount; i++)
        {
            if (!firstColumn)
            {
                commandStringBuilder.Append(", ");
            }

            commandStringBuilder.Append(helper.DelimitIdentifier(plan.GetKeyColumnName(i)));
            firstColumn = false;
        }

        for (var i = 0; i < plan.WriteColumnCount; i++)
        {
            if (!firstColumn)
            {
                commandStringBuilder.Append(", ");
            }

            commandStringBuilder.Append(helper.DelimitIdentifier(plan.GetWriteColumnName(i)));
            firstColumn = false;
        }

        commandStringBuilder.Append(')');

        // WHERE <table>.<key> = v.<key> AND ...
        commandStringBuilder.AppendLine();
        commandStringBuilder.Append("WHERE ");
        for (var i = 0; i < plan.KeyColumnCount; i++)
        {
            if (i > 0)
            {
                commandStringBuilder.Append(" AND ");
            }

            // Qualify with the (unschemed) table name so the reference resolves to the UPDATE target.
            var column = helper.DelimitIdentifier(plan.GetKeyColumnName(i));
            commandStringBuilder
                .Append(helper.DelimitIdentifier(plan.TableName))
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
        DuckDBBulkUpdatePlan plan,
        int rowIndex,
        ISqlGenerationHelper helper)
    {
        commandStringBuilder.Append('(');

        var first = true;

        // Key columns first (original values, matching the alias column order), then written values.
        for (var i = 0; i < plan.KeyColumnCount; i++)
        {
            if (!first)
            {
                commandStringBuilder.Append(", ");
            }

            commandStringBuilder.Append(helper.GenerateParameterNamePlaceholder(plan.GetOriginalKeyParameterName(rowIndex, i)));
            first = false;
        }

        for (var i = 0; i < plan.WriteColumnCount; i++)
        {
            if (!first)
            {
                commandStringBuilder.Append(", ");
            }

            commandStringBuilder.Append(helper.GenerateParameterNamePlaceholder(plan.GetWriteParameterName(rowIndex, i)));
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
        => AppendBulkDeleteOperation(
            commandStringBuilder,
            DuckDBBulkDeletePlanner.Create(modificationCommands),
            out requiresTransaction);

    internal ResultSetMapping AppendBulkDeleteOperation(
        StringBuilder commandStringBuilder,
        DuckDBBulkDeletePlan plan,
        out bool requiresTransaction)
    {
        var helper = SqlGenerationHelper;

        commandStringBuilder.Append("DELETE FROM ");
        commandStringBuilder.Append(helper.DelimitIdentifier(plan.TableName, plan.Schema));

        if (plan.KeyColumnCount == 1)
        {
            // Single-column key: DELETE FROM t WHERE <key> IN ($k0, $k1, ...)
            commandStringBuilder.Append(" WHERE ");
            commandStringBuilder.Append(helper.DelimitIdentifier(plan.GetKeyColumnName(0)));
            commandStringBuilder.Append(" IN (");

            for (var rowIndex = 0; rowIndex < plan.RowCount; rowIndex++)
            {
                if (rowIndex > 0)
                {
                    commandStringBuilder.Append(", ");
                }

                commandStringBuilder.Append(
                    helper.GenerateParameterNamePlaceholder(plan.GetOriginalKeyParameterName(rowIndex, 0)));
            }

            commandStringBuilder.Append(')');
        }
        else
        {
            // Composite key: DELETE FROM t USING (VALUES (..),(..)) AS v(k1,k2) WHERE t.k1=v.k1 AND t.k2=v.k2
            commandStringBuilder.Append(" USING (VALUES ");
            for (var rowIndex = 0; rowIndex < plan.RowCount; rowIndex++)
            {
                if (rowIndex > 0)
                {
                    commandStringBuilder.Append(", ");
                }

                commandStringBuilder.Append('(');
                for (var keyIndex = 0; keyIndex < plan.KeyColumnCount; keyIndex++)
                {
                    if (keyIndex > 0)
                    {
                        commandStringBuilder.Append(", ");
                    }

                    commandStringBuilder.Append(
                        helper.GenerateParameterNamePlaceholder(
                            plan.GetOriginalKeyParameterName(rowIndex, keyIndex)));
                }

                commandStringBuilder.Append(')');
            }

            commandStringBuilder.Append(") AS v(");
            for (var keyIndex = 0; keyIndex < plan.KeyColumnCount; keyIndex++)
            {
                if (keyIndex > 0)
                {
                    commandStringBuilder.Append(", ");
                }

                commandStringBuilder.Append(helper.DelimitIdentifier(plan.GetKeyColumnName(keyIndex)));
            }

            commandStringBuilder.Append(") WHERE ");
            for (var keyIndex = 0; keyIndex < plan.KeyColumnCount; keyIndex++)
            {
                if (keyIndex > 0)
                {
                    commandStringBuilder.Append(" AND ");
                }

                var column = helper.DelimitIdentifier(plan.GetKeyColumnName(keyIndex));
                commandStringBuilder
                    .Append(helper.DelimitIdentifier(plan.TableName))
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