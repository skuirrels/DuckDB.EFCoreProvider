using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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

            var structEntries = GroupStructuredEntries(writeOperations, table, schema);

            if (structEntries is null)
            {
                AppendBulkUpdateOperationCore(commandStringBuilder, modificationCommands, table, schema,
                    keyOperations, writeOperations, keyIndexes, writeIndexes, helper);
            }
            else
            {
                AppendBulkUpdateOperationStructured(commandStringBuilder, modificationCommands, table, schema,
                    keyOperations, keyIndexes, writeIndexes, structEntries, helper);
            }

            requiresTransaction = false;

            return ResultSetMapping.NoResults;
        }

        private void AppendBulkUpdateOperationCore(
            StringBuilder commandStringBuilder,
            IReadOnlyList<IReadOnlyModificationCommand> modificationCommands,
            string table,
            string? schema,
            List<IColumnModification> keyOperations,
            List<IColumnModification> writeOperations,
            int[] keyIndexes,
            int[] writeIndexes,
            ISqlGenerationHelper helper)
        {
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

                var column = helper.DelimitIdentifier(keyOperations[i].ColumnName);
                commandStringBuilder
                    .Append(helper.DelimitIdentifier(table))
                    .Append('.')
                    .Append(column)
                    .Append(" = v.")
                    .Append(column);
            }

            commandStringBuilder.AppendLine(helper.StatementTerminator);
        }

        private void AppendBulkUpdateOperationStructured(
            StringBuilder commandStringBuilder,
            IReadOnlyList<IReadOnlyModificationCommand> modificationCommands,
            string table,
            string? schema,
            List<IColumnModification> keyOperations,
            int[] keyIndexes,
            int[] writeIndexes,
            List<StructAwareEntry> structEntries,
            ISqlGenerationHelper helper)
        {
            // UPDATE <table> SET <col> = v.<col>, <struct> = struct_update(<struct>, ...), ...
            commandStringBuilder.Append("UPDATE ");
            commandStringBuilder.Append(helper.DelimitIdentifier(table, schema));
            commandStringBuilder.Append(" SET ");
            for (var i = 0; i < structEntries.Count; i++)
            {
                if (i > 0)
                {
                    commandStringBuilder.Append(", ");
                }

                            switch (structEntries[i])
                                            {
                                                case StandaloneEntry:
                                                    var column = helper.DelimitIdentifier(structEntries[i].ColumnName);
                                                    commandStringBuilder.Append(column).Append(" = v.").Append(column);
                                                    break;
                                                case StructGroupEntry structGroup:
                                                    var structColumn = helper.DelimitIdentifier(structGroup.StructColumnName);
                                                    commandStringBuilder.Append(structColumn).Append(" = ");
                                                    AppendStructUpdateBulk(commandStringBuilder, structColumn, structGroup.Fields, helper);
                                                    break;
                                            }
                                        }

            // FROM (VALUES (key.., struct_literal..), ...) AS v(keyCols.., structCols..)
            commandStringBuilder.AppendLine();
            commandStringBuilder.Append("FROM (VALUES ");
            for (var c = 0; c < modificationCommands.Count; c++)
            {
                if (c > 0)
                {
                    commandStringBuilder.Append(", ");
                }

                AppendBulkUpdateValuesTupleStructured(
                    commandStringBuilder, modificationCommands[c], keyIndexes, writeIndexes, helper);
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

            for (var i = 0; i < structEntries.Count; i++)
            {
                            switch (structEntries[i])
                {
                                                case StandaloneEntry standalone:
                                                    if (!firstColumn)
                                                    {
                                                        commandStringBuilder.Append(", ");
                                                    }

                                                    commandStringBuilder.Append(helper.DelimitIdentifier(standalone.ColumnName));
                                                    firstColumn = false;
                                                    break;
                                                case StructGroupEntry structGroup:
                                                    foreach (var (_, mod) in structGroup.Fields)
                                                    {
                                                        if (!firstColumn)
                                                        {
                                                            commandStringBuilder.Append(", ");
                                                        }

                                                        commandStringBuilder.Append(helper.DelimitIdentifier(mod.ColumnName));
                                                        firstColumn = false;
                                                    }
                                                    break;
                                            }
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

                var column = helper.DelimitIdentifier(keyOperations[i].ColumnName);
                commandStringBuilder
                    .Append(helper.DelimitIdentifier(table))
                    .Append('.')
                    .Append(column)
                    .Append(" = v.")
                    .Append(column);
            }

            commandStringBuilder.AppendLine(helper.StatementTerminator);
        }

        private void AppendBulkUpdateValuesTupleStructured(
            StringBuilder commandStringBuilder,
            IReadOnlyModificationCommand command,
            IReadOnlyList<int> keyIndexes,
            IReadOnlyList<int> writeIndexes,
            ISqlGenerationHelper helper)
        {
            commandStringBuilder.Append('(');

            var first = true;

            // Key columns first (original values)
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

            // Written values: group struct sub-fields into struct literals
            var writeMods = GetColumnModifications(command, writeIndexes);
            var entries = GroupStructuredEntries(writeMods, command.TableName, command.Schema);
            if (entries is null)
            {
                // No struct columns — emit individual parameters
                for (var i = 0; i < writeMods.Count; i++)
                {
                    if (!first)
                    {
                        commandStringBuilder.Append(", ");
                    }

                    commandStringBuilder.Append(helper.GenerateParameterNamePlaceholder(writeMods[i].ParameterName!));
                    first = false;
                }
            }
            else
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    if (!first)
                    {
                        commandStringBuilder.Append(", ");
                    }

                    switch (entries[i])
                    {
                        case StandaloneEntry standalone:
                            commandStringBuilder.Append(helper.GenerateParameterNamePlaceholder(standalone.Modification.ParameterName!));
                            break;
                        case StructGroupEntry structGroup:
                                                    // Emit individual sub-field parameters (not struct literals);
                            // the SET clause uses struct_update to apply them selectively.
                            foreach (var (_, mod) in structGroup.Fields)
                            {
                                if (!first)
                                {
                                    commandStringBuilder.Append(", ");
                                }

                                commandStringBuilder.Append(helper.GenerateParameterNamePlaceholder(mod.ParameterName!));
                                first = false;
                            }
                            break;
                    }

                    first = false;
                }
            }

            commandStringBuilder.Append(')');
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

        #region Struct column consolidation

        private abstract record StructAwareEntry
        {
            public abstract string ColumnName { get; }
        }

        private sealed record StandaloneEntry(IColumnModification Modification) : StructAwareEntry
        {
            public override string ColumnName => Modification.ColumnName;
        }

        private sealed record StructGroupEntry(
            string StructColumnName,
                    IReadOnlyList<(DuckDBStructFieldInfo FieldInfo, IColumnModification Modification)> Fields)
            : StructAwareEntry
        {
            public override string ColumnName => StructColumnName;
        }

        /// <summary>
        /// Groups <see cref="IColumnModification" />s by struct column, consolidating sub-property columns
        /// into single entries. Returns <see langword="null" /> when no struct columns are present (fast path).
        /// </summary>
        private List<StructAwareEntry>? GroupStructuredEntries(
            IReadOnlyList<IColumnModification> modifications,
            string tableName,
            string? schema)
        {
            var columnMap = ResolveStructColumnMap(modifications, tableName, schema);

            List<StructAwareEntry>? result = null;
            Dictionary<string, List<(DuckDBStructFieldInfo, IColumnModification)>>? structGroups = null;
            var seenStructColumns = new HashSet<string>();

            foreach (var mod in modifications)
            {
                var sfi = TryGetStructFieldInfo(mod, columnMap);
                if (sfi is null)
                {
                    continue;
                }

                result ??= [];
                structGroups ??= [];

                if (!structGroups.TryGetValue(sfi.StructColumnName, out var fields))
                {
                    fields = [];
                    structGroups[sfi.StructColumnName] = fields;
                }
                fields.Add((sfi, mod));
            }

            if (result is null || structGroups is null)
            {
                return null;
            }

            // Second pass: build ordered list with consolidated struct entries
            result.Clear();
            seenStructColumns.Clear();
            foreach (var mod in modifications)
            {
                var sfi = TryGetStructFieldInfo(mod, columnMap);
                if (sfi is null)
                {
                    result.Add(new StandaloneEntry(mod));
                }
                else if (seenStructColumns.Add(sfi.StructColumnName))
                {
                    result.Add(new StructGroupEntry(sfi.StructColumnName, structGroups[sfi.StructColumnName]));
                }
            }

            return result;
        }

        /// <summary>
        ///     Resolves the per-entity struct column map for the table referenced by these
        ///     modifications. Shared complex types (e.g. Billing and Shipping both mapped to a
        ///     shared Address type) cannot be resolved from the property annotation alone, so we
        ///     look up the correct <see cref="DuckDBStructFieldInfo" /> by the column name.
        /// </summary>
        private static IReadOnlyDictionary<string, DuckDBStructFieldInfo>? ResolveStructColumnMap(
            IReadOnlyList<IColumnModification> modifications,
            string tableName,
            string? schema)
        {
            var representative = modifications.FirstOrDefault(m => m.Property?.DeclaringType?.Model is not null);
            if (representative?.Property?.DeclaringType?.Model is not IModel model)
            {
                return null;
            }

            IReadOnlyDictionary<string, DuckDBStructFieldInfo>? firstMatch = null;
            foreach (var entityType in model.GetEntityTypes())
            {
                if (entityType.GetTableName() != tableName)
                {
                    continue;
                }

                if (entityType.GetSchema() != schema)
                {
                    continue;
                }

                var map = entityType.GetStructColumnMap();
                if (map is { Count: > 0 })
                {
                    firstMatch ??= map;
                }
            }

            return firstMatch;
        }

        /// <summary>
        ///     Returns the struct field info for a single column modification, preferring the
        ///     per-entity column map and falling back to the legacy leaf-property annotation.
        /// </summary>
        private static DuckDBStructFieldInfo? TryGetStructFieldInfo(
            IColumnModification modification,
            IReadOnlyDictionary<string, DuckDBStructFieldInfo>? columnMap)
        {
            if (columnMap?.TryGetValue(modification.ColumnName, out var info) == true)
            {
                return info;
            }

            return modification.Property?.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value
                as DuckDBStructFieldInfo;
        }

        /// <inheritdoc />
        protected override void AppendInsertCommandHeader(
            StringBuilder commandStringBuilder,
            string name,
            string? schema,
            IReadOnlyList<IColumnModification> operations)
        {
            var entries = GroupStructuredEntries(operations, name, schema);
            if (entries is null)
            {
                base.AppendInsertCommandHeader(commandStringBuilder, name, schema, operations);
                return;
            }

            var helper = SqlGenerationHelper;
            commandStringBuilder
                .Append("INSERT INTO ")
                .Append(helper.DelimitIdentifier(name, schema))
                .Append(" (");

            for (var i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                {
                    commandStringBuilder.Append(", ");
                }

                commandStringBuilder.Append(helper.DelimitIdentifier(entries[i].ColumnName));
            }

            commandStringBuilder.Append(')');
        }

        /// <inheritdoc />
        protected override void AppendValues(
            StringBuilder commandStringBuilder,
            string name,
            string? schema,
            IReadOnlyList<IColumnModification> operations)
        {
            var entries = GroupStructuredEntries(operations, name, schema);
            if (entries is null)
            {
                base.AppendValues(commandStringBuilder, name, schema, operations);
                return;
            }

            var helper = SqlGenerationHelper;
            commandStringBuilder.Append('(');

            for (var i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                {
                    commandStringBuilder.Append(", ");
                }

                switch (entries[i])
                {
                    case StandaloneEntry standalone:
                        commandStringBuilder.Append(helper.GenerateParameterNamePlaceholder(standalone.Modification.ParameterName!));
                        break;
                    case StructGroupEntry structGroup:
                        AppendStructLiteral(commandStringBuilder, structGroup.Fields, helper);
                        break;
                }
            }

            commandStringBuilder.Append(')');
        }

        /// <inheritdoc />
        protected override void AppendUpdateCommandHeader(
            StringBuilder commandStringBuilder,
            string name,
            string? schema,
            IReadOnlyList<IColumnModification> operations)
        {
            var entries = GroupStructuredEntries(operations, name, schema);
            if (entries is null)
            {
                base.AppendUpdateCommandHeader(commandStringBuilder, name, schema, operations);
                return;
            }

            var helper = SqlGenerationHelper;
            commandStringBuilder
                .Append("UPDATE ")
                .Append(helper.DelimitIdentifier(name, schema))
                .Append(" SET ");

            for (var i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                {
                    commandStringBuilder.Append(", ");
                }

                var columnName = helper.DelimitIdentifier(entries[i].ColumnName);
                switch (entries[i])
                {
                    case StandaloneEntry standalone:
                        commandStringBuilder.Append(columnName).Append(" = ").Append(
                            helper.GenerateParameterNamePlaceholder(standalone.Modification.ParameterName!));
                        break;
                    case StructGroupEntry structGroup:
                        commandStringBuilder.Append(columnName).Append(" = ");
                                            AppendStructUpdate(commandStringBuilder, columnName, structGroup.Fields, helper);
                                            break;
                                    }
            }
        }

        private static StructLiteralNode BuildStructTree(
            IReadOnlyList<(DuckDBStructFieldInfo FieldInfo, IColumnModification Modification)> fields)
        {
            // Build a tree from the flat field list using NestedFieldNames, so that intermediate
            // struct levels (e.g. shipping.address.street) are rendered as nested struct expressions
            // matching the DDL: {'method': @p0, 'address': {'street': @p1, 'zip': @p2}}.
            var root = new StructLiteralNode();
            foreach (var (fieldInfo, mod) in fields)
            {
                var current = root;
                foreach (var nestedName in fieldInfo.NestedFieldNames)
                {
                    var child = current.Children.Find(c => c.FieldName == nestedName);
                    if (child is null)
                    {
                        child = new StructLiteralNode { FieldName = nestedName };
                        current.Children.Add(child);
                    }
                    current = child;
                }

                current.Children.Add(new StructLiteralNode
                {
                    FieldName = fieldInfo.LeafFieldName ?? mod.ColumnName,
                    ParameterName = mod.ParameterName,
                    ColumnName = mod.ColumnName
                });
            }

            return root;
        }

        /// <summary>
        /// Renders a full struct literal for INSERT: {'field': @param, 'nested': {'leaf': @param}}.
        /// All sub-fields must be present.
        /// </summary>
        private static void AppendStructLiteral(
            StringBuilder sb,
            IReadOnlyList<(DuckDBStructFieldInfo FieldInfo, IColumnModification Modification)> fields,
            ISqlGenerationHelper helper)
        {
            var root = BuildStructTree(fields);
            RenderStructLiteralNode(sb, root, helper);
        }

        private static void RenderStructLiteralNode(StringBuilder sb, StructLiteralNode node, ISqlGenerationHelper helper)
        {
            sb.Append('{');
            for (var i = 0; i < node.Children.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                var child = node.Children[i];
                sb.Append('\'').Append(child.FieldName).Append("': ");
                if (child.ParameterName is not null)
                {
                    sb.Append(helper.GenerateParameterNamePlaceholder(child.ParameterName));
                }
                else
                {
                    RenderStructLiteralNode(sb, child, helper);
                }
            }

            sb.Append('}');
        }

        /// <summary>
        /// Renders a DuckDB <c>struct_update()</c> expression for partial UPDATE: only the modified
        /// sub-fields are targeted, preserving all other fields. Supports nested structs by chaining
        /// <c>struct_update(struct_extract(...), ...)</c> for intermediate levels.
        /// </summary>
        /// <example>
        /// Single level:  <c>struct_update("location", 'city', @p0)</c>
        /// Nested:        <c>struct_update("shipping", 'method', @p0, 'address', struct_update(struct_extract("shipping", 'address'), 'street', @p1))</c>
        /// Multiple fields: <c>struct_update("location", 'city', @p0, 'country', @p1)</c>
        /// </example>
        private static void AppendStructUpdate(
            StringBuilder sb,
            string columnRef,
            IReadOnlyList<(DuckDBStructFieldInfo FieldInfo, IColumnModification Modification)> fields,
            ISqlGenerationHelper helper)
        {
            var root = BuildStructTree(fields);
            RenderStructUpdate(sb, root, columnRef, helper, useParameterPlaceholder: true);
        }

        /// <summary>
        /// Renders a DuckDB <c>struct_update()</c> for bulk UPDATE where leaf values come from
        /// the VALUES subquery alias <c>v."col_name"</c> instead of parameter placeholders.
        /// </summary>
        private static void AppendStructUpdateBulk(
            StringBuilder sb,
            string columnRef,
            IReadOnlyList<(DuckDBStructFieldInfo FieldInfo, IColumnModification Modification)> fields,
            ISqlGenerationHelper helper)
        {
            var root = BuildStructTree(fields);
            RenderStructUpdate(sb, root, columnRef, helper, useParameterPlaceholder: false);
        }

        private static void RenderStructUpdate(
            StringBuilder sb,
            StructLiteralNode node,
            string columnRef,
            ISqlGenerationHelper helper,
            bool useParameterPlaceholder)
        {
            sb.Append("struct_update(");
            sb.Append(columnRef);

            for (var i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                // DuckDB struct_update uses named-argument syntax: field := value
                sb.Append(", ").Append(child.FieldName).Append(" := ");

                if (child.ParameterName is not null)
                {
                    // Leaf node: emit parameter placeholder (@p0) or VALUES subquery ref (v."col")
                    if (useParameterPlaceholder)
                    {
                        sb.Append(helper.GenerateParameterNamePlaceholder(child.ParameterName));
                    }
                    else
                    {
                        sb.Append("v.").Append(helper.DelimitIdentifier(child.ColumnName!));
                    }
                }
                else
                {
                    // Intermediate node: chain struct_update on the nested struct value
                    var nestedRef = "struct_extract(" + columnRef + ", '" + child.FieldName + "')";
                    RenderStructUpdate(sb, child, nestedRef, helper, useParameterPlaceholder);
                }
            }

            sb.Append(')');
        }

        private sealed class StructLiteralNode
        {
            public string? FieldName { get; set; }
            public string? ParameterName { get; set; }
                    public string? ColumnName { get; set; }
                    public List<StructLiteralNode> Children { get; } = [];
                }

        #endregion
    }
