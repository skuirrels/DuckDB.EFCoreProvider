using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Internal;
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
    private readonly IDuckDBEngineCapabilities _capabilities;

    public DuckDBUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies)
        : this(dependencies, null, DuckDBEngineCapabilities.Native)
    {
    }

    public DuckDBUpdateSqlGenerator(
        UpdateSqlGeneratorDependencies dependencies,
        IDuckLakeSingletonOptions? singletonOptions)
        : this(
            dependencies,
            singletonOptions,
            DuckDBEngineCapabilities.FromDuckLakeOptions(singletonOptions))
    {
    }

    public DuckDBUpdateSqlGenerator(
        UpdateSqlGeneratorDependencies dependencies,
        IDuckLakeSingletonOptions? singletonOptions,
        IDuckDBEngineCapabilities? capabilities)
        : base(dependencies)
    {
        _ = singletonOptions;
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    private static DuckDBStructMutationPlan? TryGetStructPlan(
        IReadOnlyList<IColumnModification> operations,
        DuckDBStructMutationMode mode)
    {
        DuckDBStructMutationPlan.TryCreate(operations, mode, out var plan);
        return plan;
    }

    /// <inheritdoc />
    protected override void AppendInsertCommand(
        StringBuilder commandStringBuilder,
        string name,
        string? schema,
        IReadOnlyList<IColumnModification> writeOperations,
        IReadOnlyList<IColumnModification> readOperations)
    {
        var plan = TryGetStructPlan(writeOperations, DuckDBStructMutationMode.Insert);
        if (plan is null)
        {
            base.AppendInsertCommand(commandStringBuilder, name, schema, writeOperations, readOperations);
            return;
        }

        AppendStructInsertCommandHeader(commandStringBuilder, name, schema, plan);
        AppendValuesHeader(commandStringBuilder, writeOperations);
        AppendStructValues(commandStringBuilder, plan);
        AppendReturningClause(commandStringBuilder, readOperations);
        commandStringBuilder.AppendLine(SqlGenerationHelper.StatementTerminator);
    }

    /// <inheritdoc />
    protected override void AppendUpdateCommand(
        StringBuilder commandStringBuilder,
        string name,
        string? schema,
        IReadOnlyList<IColumnModification> writeOperations,
        IReadOnlyList<IColumnModification> readOperations,
        IReadOnlyList<IColumnModification> conditionOperations,
        bool appendReturningOneClause = false)
    {
        var plan = TryGetStructPlan(writeOperations, DuckDBStructMutationMode.Update);
        if (plan is null)
        {
            base.AppendUpdateCommand(
                commandStringBuilder,
                name,
                schema,
                writeOperations,
                readOperations,
                conditionOperations,
                appendReturningOneClause);
            return;
        }

        AppendStructUpdateCommandHeader(commandStringBuilder, name, schema, plan);
        AppendWhereClause(commandStringBuilder, conditionOperations);
        AppendReturningClause(
            commandStringBuilder,
            readOperations,
            appendReturningOneClause ? "1" : null);
        commandStringBuilder.AppendLine(SqlGenerationHelper.StatementTerminator);
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
        var dualRolePlan = DuckDBDualRoleUpdatePlanner.Create(command, _capabilities);
        if (dualRolePlan.RequiresConditionalForeignKeyUpdate)
        {
            return AppendConditionalForeignKeyUpdateOperation(
                commandStringBuilder,
                dualRolePlan,
                out requiresTransaction);
        }

        if (_capabilities.SupportsReturning)
        {
            if (!_capabilities.SupportsReturningOnReferencedTableUpdates
                && DuckDBUpdateFallbackPlanner.TryCreate(command, _capabilities, out var plan))
            {
                return AppendUpdateFallbackOperation(
                    commandStringBuilder,
                    plan,
                    out requiresTransaction);
            }

            return base.AppendUpdateOperation(commandStringBuilder, command, commandPosition, out requiresTransaction);
        }

        var operations = command.ColumnModifications;
        var readOperations = operations.Where(operation => operation.IsRead).ToList();
        EnsureNoStoreGeneratedValues(command, readOperations);

        AppendUpdateCommand(
            commandStringBuilder,
            command.TableName,
            command.Schema,
            dualRolePlan.WriteOperations,
            [],
            operations.Where(operation => operation.IsCondition).ToList());
        // A capability-limited backend may not physically enforce EF's logical keys. If duplicate key rows exist,
        // an update can affect more than one row and the modification batch detects that only after execution.
        // Require a transaction so EF can roll the statement back before surfacing DbUpdateConcurrencyException.
        requiresTransaction = true;
        return ResultSetMapping.NoResults;
    }

    internal ResultSetMapping AppendUpdateFallbackOperation(
        StringBuilder commandStringBuilder,
        DuckDBUpdateFallbackPlan plan,
        out bool requiresTransaction)
    {
        AppendUpdateCommand(
            commandStringBuilder,
            plan.TableName,
            plan.Schema,
            plan.WriteOperations,
            [],
            plan.ConditionOperations);

        requiresTransaction = plan.ReadOperations.Count > 0 || plan.HasChangedForeignKeyWrites;
        return ResultSetMapping.NoResults;
    }

    private ResultSetMapping AppendConditionalForeignKeyUpdateOperation(
        StringBuilder commandStringBuilder,
        DuckDBDualRoleUpdatePlan plan,
        out bool requiresTransaction)
    {
        commandStringBuilder
            .Append("UPDATE ")
            .Append(SqlGenerationHelper.DelimitIdentifier(plan.Command.TableName, plan.Command.Schema))
            .Append(" SET ");
        for (var i = 0; i < plan.UnchangedForeignKeyWrites.Count; i++)
        {
            if (i > 0)
            {
                commandStringBuilder.Append(", ");
            }

            var modification = plan.UnchangedForeignKeyWrites[i];
            commandStringBuilder
                .Append(SqlGenerationHelper.DelimitIdentifier(modification.ColumnName))
                .Append(" = ");
            AppendUpdateColumnValue(
                SqlGenerationHelper,
                modification,
                commandStringBuilder,
                plan.Command.TableName,
                plan.Command.Schema);
        }

        var conditions = plan.Command.ColumnModifications
            .Where(modification => modification.IsCondition)
            .ToArray();
        commandStringBuilder.AppendLine().Append("WHERE ");
        AppendConditions(commandStringBuilder, conditions);
        commandStringBuilder.Append(" AND (");
        for (var i = 0; i < plan.UnchangedForeignKeyWrites.Count; i++)
        {
            if (i > 0)
            {
                commandStringBuilder.Append(" OR ");
            }

            var modification = plan.UnchangedForeignKeyWrites[i];
            commandStringBuilder
                .Append(SqlGenerationHelper.DelimitIdentifier(modification.ColumnName))
                .Append(" IS DISTINCT FROM ");
            AppendUpdateColumnValue(
                SqlGenerationHelper,
                modification,
                commandStringBuilder,
                plan.Command.TableName,
                plan.Command.Schema);
        }

        commandStringBuilder
            .AppendLine(")" + SqlGenerationHelper.StatementTerminator)
            .Append("SELECT CAST(COUNT(*) AS INTEGER)")
            .AppendLine()
            .Append("FROM ")
            .Append(SqlGenerationHelper.DelimitIdentifier(plan.Command.TableName, plan.Command.Schema))
            .AppendLine()
            .Append("WHERE ");
        AppendConditions(commandStringBuilder, conditions);
        commandStringBuilder.AppendLine(SqlGenerationHelper.StatementTerminator);

        requiresTransaction = true;
        return ResultSetMapping.LastInResultSet | ResultSetMapping.ResultSetWithRowsAffectedOnly;
    }

    private void AppendConditions(
        StringBuilder commandStringBuilder,
        IReadOnlyList<IColumnModification> conditions)
    {
        for (var i = 0; i < conditions.Count; i++)
        {
            if (i > 0)
            {
                commandStringBuilder.Append(" AND ");
            }

            AppendWhereCondition(
                commandStringBuilder,
                conditions[i],
                conditions[i].UseOriginalValueParameter);
        }
    }

    internal void AppendUpdateFallbackReadbackCommand(
        StringBuilder commandStringBuilder,
        DuckDBUpdateFallbackPlan plan)
    {
        commandStringBuilder.Append("SELECT ");
        for (var i = 0; i < plan.ReadOperations.Count; i++)
        {
            if (i > 0)
            {
                commandStringBuilder.Append(", ");
            }

            commandStringBuilder.Append(
                SqlGenerationHelper.DelimitIdentifier(plan.ReadOperations[i].ColumnName));
        }

        commandStringBuilder
            .AppendLine()
            .Append("FROM ")
            .Append(SqlGenerationHelper.DelimitIdentifier(plan.TableName, plan.Schema))
            .AppendLine()
            .Append("WHERE ");

        for (var i = 0; i < plan.KeyOperations.Count; i++)
        {
            if (i > 0)
            {
                commandStringBuilder.Append(" AND ");
            }

            var keyOperation = plan.KeyOperations[i];
            AppendWhereCondition(
                commandStringBuilder,
                keyOperation,
                keyOperation.UseOriginalValueParameter);
        }

        commandStringBuilder.AppendLine(SqlGenerationHelper.StatementTerminator);
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
                DuckDBCapabilityErrorMessages.StoreGeneratedColumnsCannotBeRead(
                    command.TableName,
                    readOperations.Select(operation => operation.ColumnName)));
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

        var firstMutationPlan = plan.GetStructMutationPlan(0);
        if (firstMutationPlan is null)
        {
            AppendInsertCommandHeader(commandStringBuilder, plan.TableName, plan.Schema, writeOperations);
        }
        else
        {
            AppendStructInsertCommandHeader(
                commandStringBuilder,
                plan.TableName,
                plan.Schema,
                firstMutationPlan);
        }

        AppendValuesHeader(commandStringBuilder, writeOperations);
        if (firstMutationPlan is null)
        {
            AppendValues(commandStringBuilder, plan.TableName, plan.Schema, writeOperations);
        }
        else
        {
            AppendStructValues(commandStringBuilder, firstMutationPlan);
        }

        for (var rowIndex = 1; rowIndex < plan.RowCount; rowIndex++)
        {
            commandStringBuilder.AppendLine(",");
            plan.CollectWriteColumns(rowIndex, writeOperations);
            var mutationPlan = plan.GetStructMutationPlan(rowIndex);
            if (mutationPlan is null)
            {
                AppendValues(
                    commandStringBuilder,
                    plan.TableName,
                    plan.Schema,
                    writeOperations);
            }
            else
            {
                AppendStructValues(commandStringBuilder, mutationPlan);
            }
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
            DuckDBBulkUpdatePlanner.Create(modificationCommands, _capabilities),
            out requiresTransaction);

    internal ResultSetMapping AppendBulkUpdateOperation(
        StringBuilder commandStringBuilder,
        DuckDBBulkUpdatePlan plan,
        out bool requiresTransaction)
    {
        var helper = SqlGenerationHelper;
        var structMutationPlan = plan.StructMutationPlan;

        if (structMutationPlan is null)
        {
            AppendBulkUpdateOperationCore(commandStringBuilder, plan, helper);
        }
        else
        {
            AppendBulkUpdateOperationStructured(
                commandStringBuilder,
                plan,
                structMutationPlan,
                helper);
        }

        requiresTransaction = false;
        return ResultSetMapping.NoResults;
    }

    private static void AppendBulkUpdateOperationCore(
        StringBuilder commandStringBuilder,
        DuckDBBulkUpdatePlan plan,
        ISqlGenerationHelper helper)
    {
        commandStringBuilder
            .Append("UPDATE ")
            .Append(helper.DelimitIdentifier(plan.TableName, plan.Schema))
            .Append(" SET ");

        for (var i = 0; i < plan.WriteColumnCount; i++)
        {
            if (i > 0)
            {
                commandStringBuilder.Append(", ");
            }

            var column = helper.DelimitIdentifier(plan.GetWriteColumnName(i));
            commandStringBuilder.Append(column).Append(" = v.").Append(column);
        }

        commandStringBuilder.AppendLine().Append("FROM (VALUES ");
        for (var rowIndex = 0; rowIndex < plan.RowCount; rowIndex++)
        {
            if (rowIndex > 0)
            {
                commandStringBuilder.Append(", ");
            }

            AppendBulkUpdateValuesTuple(commandStringBuilder, plan, rowIndex, helper);
        }

        AppendBulkUpdateAliasAndPredicate(commandStringBuilder, plan, helper);
    }

    private void AppendBulkUpdateOperationStructured(
        StringBuilder commandStringBuilder,
        DuckDBBulkUpdatePlan plan,
        DuckDBStructMutationPlan mutationPlan,
        ISqlGenerationHelper helper)
    {
        commandStringBuilder
            .Append("UPDATE ")
            .Append(helper.DelimitIdentifier(plan.TableName, plan.Schema))
            .Append(" SET ");

        for (var i = 0; i < mutationPlan.Entries.Count; i++)
        {
            if (i > 0)
            {
                commandStringBuilder.Append(", ");
            }

            switch (mutationPlan.Entries[i])
            {
                case DuckDBStandaloneMutationEntry standalone:
                    var column = helper.DelimitIdentifier(standalone.ColumnName);
                    commandStringBuilder.Append(column).Append(" = v.").Append(column);
                    break;
                case DuckDBStructMutationGroup structGroup:
                    var structColumn = helper.DelimitIdentifier(structGroup.StructColumnName);
                    commandStringBuilder.Append(structColumn).Append(" = ");
                    AppendStructUpdateBulk(commandStringBuilder, structColumn, structGroup.Root, helper);
                    break;
            }
        }

        var structuredWriteOrdinals = GetStructuredWriteOrdinals(mutationPlan);

        commandStringBuilder.AppendLine().Append("FROM (VALUES ");
        for (var rowIndex = 0; rowIndex < plan.RowCount; rowIndex++)
        {
            if (rowIndex > 0)
            {
                commandStringBuilder.Append(", ");
            }

            AppendBulkUpdateValuesTuple(
                commandStringBuilder,
                plan,
                rowIndex,
                structuredWriteOrdinals,
                helper);
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

        foreach (var entry in mutationPlan.Entries)
        {
            switch (entry)
            {
                case DuckDBStandaloneMutationEntry standalone:
                    if (!firstColumn)
                    {
                        commandStringBuilder.Append(", ");
                    }

                    commandStringBuilder.Append(helper.DelimitIdentifier(standalone.ColumnName));
                    firstColumn = false;
                    break;
                case DuckDBStructMutationGroup structGroup:
                    foreach (var leaf in GetLeaves(structGroup.Root))
                    {
                        if (!firstColumn)
                        {
                            commandStringBuilder.Append(", ");
                        }

                        commandStringBuilder.Append(helper.DelimitIdentifier(leaf.ColumnName!));
                        firstColumn = false;
                    }
                    break;
            }
        }

        AppendBulkUpdatePredicate(commandStringBuilder.Append(')'), plan, helper);
    }

    private static int[] GetStructuredWriteOrdinals(DuckDBStructMutationPlan mutationPlan)
    {
        var ordinals = new List<int>();
        foreach (var entry in mutationPlan.Entries)
        {
            switch (entry)
            {
                case DuckDBStandaloneMutationEntry standalone:
                    ordinals.Add(standalone.WriteOrdinal);
                    break;
                case DuckDBStructMutationGroup structGroup:
                    foreach (var leaf in GetLeaves(structGroup.Root))
                    {
                        ordinals.Add(leaf.WriteOrdinal!.Value);
                    }
                    break;
            }
        }

        return ordinals.ToArray();
    }

    private static IEnumerable<DuckDBStructMutationNode> GetLeaves(DuckDBStructMutationNode node)
    {
        foreach (var child in node.Children)
        {
            if (child.IsLeaf)
            {
                yield return child;
            }
            else
            {
                foreach (var leaf in GetLeaves(child))
                {
                    yield return leaf;
                }
            }
        }
    }

    private static void AppendBulkUpdateValuesTuple(
        StringBuilder commandStringBuilder,
        DuckDBBulkUpdatePlan plan,
        int rowIndex,
        ISqlGenerationHelper helper)
        => AppendBulkUpdateValuesTuple(commandStringBuilder, plan, rowIndex, writeOrdinals: null, helper);

    private static void AppendBulkUpdateValuesTuple(
        StringBuilder commandStringBuilder,
        DuckDBBulkUpdatePlan plan,
        int rowIndex,
        IReadOnlyList<int>? writeOrdinals,
        ISqlGenerationHelper helper)
    {
        commandStringBuilder.Append('(');
        var first = true;

        for (var i = 0; i < plan.KeyColumnCount; i++)
        {
            if (!first)
            {
                commandStringBuilder.Append(", ");
            }

            commandStringBuilder.Append(
                helper.GenerateParameterNamePlaceholder(plan.GetOriginalKeyParameterName(rowIndex, i)));
            first = false;
        }

        var writeCount = writeOrdinals?.Count ?? plan.WriteColumnCount;
        for (var i = 0; i < writeCount; i++)
        {
            if (!first)
            {
                commandStringBuilder.Append(", ");
            }

            var writeOrdinal = writeOrdinals?[i] ?? i;
            commandStringBuilder.Append(
                helper.GenerateParameterNamePlaceholder(plan.GetWriteParameterName(rowIndex, writeOrdinal)));
            first = false;
        }

        commandStringBuilder.Append(')');
    }

    private static void AppendBulkUpdateAliasAndPredicate(
        StringBuilder commandStringBuilder,
        DuckDBBulkUpdatePlan plan,
        ISqlGenerationHelper helper)
    {
        commandStringBuilder.Append(") AS v(");
        var firstColumn = true;
        for (var i = 0; i < plan.KeyColumnCount; i++)
        {
            AppendBulkUpdateAliasColumn(
                commandStringBuilder,
                plan.GetKeyColumnName(i),
                ref firstColumn,
                helper);
        }

        for (var i = 0; i < plan.WriteColumnCount; i++)
        {
            AppendBulkUpdateAliasColumn(
                commandStringBuilder,
                plan.GetWriteColumnName(i),
                ref firstColumn,
                helper);
        }

        AppendBulkUpdatePredicate(commandStringBuilder.Append(')'), plan, helper);
    }

    private static void AppendBulkUpdateAliasColumn(
        StringBuilder commandStringBuilder,
        string columnName,
        ref bool firstColumn,
        ISqlGenerationHelper helper)
    {
        if (!firstColumn)
        {
            commandStringBuilder.Append(", ");
        }

        commandStringBuilder.Append(helper.DelimitIdentifier(columnName));
        firstColumn = false;
    }

    private static void AppendBulkUpdatePredicate(
        StringBuilder commandStringBuilder,
        DuckDBBulkUpdatePlan plan,
        ISqlGenerationHelper helper)
    {
        commandStringBuilder.AppendLine().Append("WHERE ");
        for (var i = 0; i < plan.KeyColumnCount; i++)
        {
            if (i > 0)
            {
                commandStringBuilder.Append(" AND ");
            }

            var column = helper.DelimitIdentifier(plan.GetKeyColumnName(i));
            commandStringBuilder
                .Append(helper.DelimitIdentifier(plan.TableName))
                .Append('.')
                .Append(column)
                .Append(" = v.")
                .Append(column);
        }

        commandStringBuilder.AppendLine(helper.StatementTerminator);
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

    private void AppendStructInsertCommandHeader(
        StringBuilder commandStringBuilder,
        string name,
        string? schema,
        DuckDBStructMutationPlan plan)
    {
        var helper = SqlGenerationHelper;
        commandStringBuilder
            .Append("INSERT INTO ")
            .Append(helper.DelimitIdentifier(name, schema))
            .Append(" (");

        for (var i = 0; i < plan.Entries.Count; i++)
        {
            if (i > 0)
            {
                commandStringBuilder.Append(", ");
            }

            commandStringBuilder.Append(helper.DelimitIdentifier(plan.Entries[i].ColumnName));
        }

        commandStringBuilder.Append(')');
    }

    private void AppendStructValues(
        StringBuilder commandStringBuilder,
        DuckDBStructMutationPlan plan)
    {
        var helper = SqlGenerationHelper;
        commandStringBuilder.Append('(');

        for (var i = 0; i < plan.Entries.Count; i++)
        {
            if (i > 0)
            {
                commandStringBuilder.Append(", ");
            }

            switch (plan.Entries[i])
            {
                case DuckDBStandaloneMutationEntry standalone:
                    commandStringBuilder.Append(
                        helper.GenerateParameterNamePlaceholder(standalone.ParameterName));
                    break;
                case DuckDBStructMutationGroup structGroup:
                    if (structGroup.IsNull)
                    {
                        commandStringBuilder.Append("NULL");
                    }
                    else
                    {
                        AppendStructLiteral(commandStringBuilder, structGroup.Root, helper);
                    }
                    break;
            }
        }

        commandStringBuilder.Append(')');
    }

    private void AppendStructUpdateCommandHeader(
        StringBuilder commandStringBuilder,
        string name,
        string? schema,
        DuckDBStructMutationPlan plan)
    {
        var helper = SqlGenerationHelper;
        commandStringBuilder
            .Append("UPDATE ")
            .Append(helper.DelimitIdentifier(name, schema))
            .Append(" SET ");

        for (var i = 0; i < plan.Entries.Count; i++)
        {
            if (i > 0)
            {
                commandStringBuilder.Append(", ");
            }

            var columnName = helper.DelimitIdentifier(plan.Entries[i].ColumnName);
            switch (plan.Entries[i])
            {
                case DuckDBStandaloneMutationEntry standalone:
                    commandStringBuilder.Append(columnName).Append(" = ").Append(
                        helper.GenerateParameterNamePlaceholder(standalone.ParameterName));
                    break;
                case DuckDBStructMutationGroup structGroup:
                    commandStringBuilder.Append(columnName).Append(" = ");
                    if (structGroup.IsNull)
                    {
                        commandStringBuilder.Append("NULL");
                    }
                    else
                    {
                        AppendStructUpdate(commandStringBuilder, columnName, structGroup.Root, helper);
                    }
                    break;
            }
        }
    }

    private void AppendStructLiteral(
        StringBuilder sb,
        DuckDBStructMutationNode root,
        ISqlGenerationHelper helper)
        => RenderStructLiteralNode(sb, root, helper);

    private void RenderStructLiteralNode(
        StringBuilder sb,
        DuckDBStructMutationNode node,
        ISqlGenerationHelper helper)
    {
        sb.Append('{');
        for (var i = 0; i < node.Children.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }
            var child = node.Children[i];
            AppendStringLiteral(sb, child.FieldName!);
            sb.Append(": ");
            if (child.IsLeaf)
            {
                sb.Append(helper.GenerateParameterNamePlaceholder(child.ParameterName!));
            }
            else
            {
                RenderStructLiteralNode(sb, child, helper);
            }
        }

        sb.Append('}');
    }

    private void AppendStructUpdate(
        StringBuilder sb,
        string columnRef,
        DuckDBStructMutationNode root,
        ISqlGenerationHelper helper)
        => RenderStructUpdate(
            sb,
            root,
            columnRef,
            [],
            helper,
            useParameterPlaceholder: true);

    private void AppendStructUpdateBulk(
        StringBuilder sb,
        string columnRef,
        DuckDBStructMutationNode root,
        ISqlGenerationHelper helper)
        => RenderStructUpdate(
            sb,
            root,
            columnRef,
            [],
            helper,
            useParameterPlaceholder: false);

    private void RenderStructUpdate(
        StringBuilder sb,
        DuckDBStructMutationNode node,
        string rootColumnRef,
        IReadOnlyList<string> sourcePath,
        ISqlGenerationHelper helper,
        bool useParameterPlaceholder)
    {
        sb.Append("struct_update(");
        AppendStructSource(sb, rootColumnRef, sourcePath);

        foreach (var child in node.Children)
        {
            sb.Append(", ")
                .Append(helper.DelimitIdentifier(child.FieldName!))
                .Append(" := ");

            if (child.IsLeaf)
            {
                if (useParameterPlaceholder)
                {
                    sb.Append(helper.GenerateParameterNamePlaceholder(child.ParameterName!));
                }
                else
                {
                    sb.Append("v.").Append(helper.DelimitIdentifier(child.ColumnName!));
                }
            }
            else
            {
                RenderStructUpdate(
                    sb,
                    child,
                    rootColumnRef,
                    sourcePath.Append(child.FieldName!).ToArray(),
                    helper,
                    useParameterPlaceholder);
            }
        }

        sb.Append(')');
    }

    private void AppendStructSource(
        StringBuilder sb,
        string rootColumnRef,
        IReadOnlyList<string> sourcePath)
    {
        if (sourcePath.Count == 0)
        {
            sb.Append(rootColumnRef);
            return;
        }

        sb.Append("struct_extract(");
        AppendStructSource(sb, rootColumnRef, sourcePath.Take(sourcePath.Count - 1).ToArray());
        sb.Append(", ");
        AppendStringLiteral(sb, sourcePath[^1]);
        sb.Append(')');
    }

    private void AppendStringLiteral(StringBuilder sb, string value)
        => sb.Append(Dependencies.TypeMappingSource.FindMapping(typeof(string))!.GenerateSqlLiteral(value));

}