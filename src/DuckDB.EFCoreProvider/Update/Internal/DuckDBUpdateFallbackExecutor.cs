using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using System.Data;
using System.Text;

namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     Executes the update and generated-value read-back as separate commands while preserving EF Core's
///     affected-row, interceptor, result-propagation, and transaction semantics.
/// </summary>
internal static class DuckDBUpdateFallbackExecutor
{
    public static void Execute(
        ModificationCommandBatchFactoryDependencies dependencies,
        DuckDBUpdateSqlGenerator updateSqlGenerator,
        IRelationalConnection connection,
        RawSqlCommand? updateCommand,
        IReadOnlyList<IReadOnlyModificationCommand> modificationCommands,
        IDuckDBEngineCapabilities capabilities)
    {
        if (updateCommand is null)
        {
            throw new InvalidOperationException(RelationalStrings.ModificationCommandBatchNotComplete);
        }

        try
        {
            var affectedRows = updateCommand.RelationalCommand.ExecuteNonQuery(
                CreateParameterObject(dependencies, connection, updateCommand.ParameterValues));
            if (!DuckDBAffectedRowsValidator.Validate(dependencies, modificationCommands, affectedRows))
            {
                return;
            }

            var plan = DuckDBUpdateFallbackPlanner.Create(modificationCommands[0], capabilities);
            if (plan.ReadOperations.Count > 0)
            {
                using var reader = CreateReadbackCommand(dependencies, updateSqlGenerator, plan).ExecuteReader(
                    CreateParameterObject(dependencies, connection, updateCommand.ParameterValues));
                PropagateReadback(plan, reader);
            }
        }
        catch (Exception exception) when (exception is not DbUpdateException and not OperationCanceledException)
        {
            throw CreateDbUpdateException(exception, modificationCommands, capabilities);
        }
    }

    public static async Task ExecuteAsync(
        ModificationCommandBatchFactoryDependencies dependencies,
        DuckDBUpdateSqlGenerator updateSqlGenerator,
        IRelationalConnection connection,
        RawSqlCommand? updateCommand,
        IReadOnlyList<IReadOnlyModificationCommand> modificationCommands,
        IDuckDBEngineCapabilities capabilities,
        CancellationToken cancellationToken)
    {
        if (updateCommand is null)
        {
            throw new InvalidOperationException(RelationalStrings.ModificationCommandBatchNotComplete);
        }

        try
        {
            var parameterObject = CreateParameterObject(dependencies, connection, updateCommand.ParameterValues);
            var affectedRows = await updateCommand.RelationalCommand
                .ExecuteNonQueryAsync(parameterObject, cancellationToken)
                .ConfigureAwait(false);
            if (!await DuckDBAffectedRowsValidator
                    .ValidateAsync(dependencies, modificationCommands, affectedRows, cancellationToken)
                    .ConfigureAwait(false))
            {
                return;
            }

            var plan = DuckDBUpdateFallbackPlanner.Create(modificationCommands[0], capabilities);
            if (plan.ReadOperations.Count > 0)
            {
                var reader = await CreateReadbackCommand(dependencies, updateSqlGenerator, plan)
                    .ExecuteReaderAsync(parameterObject, cancellationToken)
                    .ConfigureAwait(false);
                await using var _ = reader.ConfigureAwait(false);
                await PropagateReadbackAsync(plan, reader, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not DbUpdateException and not OperationCanceledException)
        {
            throw CreateDbUpdateException(exception, modificationCommands, capabilities);
        }
    }

    private static RelationalCommandParameterObject CreateParameterObject(
        ModificationCommandBatchFactoryDependencies dependencies,
        IRelationalConnection connection,
        IReadOnlyDictionary<string, object?> parameterValues)
        => new(
            connection,
            parameterValues,
            null,
            dependencies.CurrentContext.Context,
            dependencies.Logger,
            CommandSource.SaveChanges);

    private static IRelationalCommand CreateReadbackCommand(
        ModificationCommandBatchFactoryDependencies dependencies,
        DuckDBUpdateSqlGenerator updateSqlGenerator,
        DuckDBUpdateFallbackPlan plan)
    {
        var builder = dependencies.CommandBuilderFactory.Create();
        var sql = new StringBuilder();
        updateSqlGenerator.AppendUpdateFallbackReadbackCommand(sql, plan);
        builder.Append(sql.ToString());

        foreach (var keyOperation in plan.KeyOperations)
        {
            if (!keyOperation.UseParameter)
            {
                continue;
            }

            var parameterName = keyOperation.UseOriginalValueParameter
                ? keyOperation.OriginalParameterName!
                : keyOperation.ParameterName!;
            builder.AddParameter(
                parameterName,
                dependencies.SqlGenerationHelper.GenerateParameterName(parameterName),
                keyOperation.TypeMapping!,
                keyOperation.IsNullable,
                ParameterDirection.Input);
        }

        return builder.Build();
    }

    private static void PropagateReadback(
        DuckDBUpdateFallbackPlan plan,
        RelationalDataReader reader)
    {
        if (!reader.Read())
        {
            throw new InvalidOperationException(RelationalStrings.MissingResultSetWhenSaving);
        }

        plan.Command.PropagateResults(reader);
    }

    private static async Task PropagateReadbackAsync(
        DuckDBUpdateFallbackPlan plan,
        RelationalDataReader reader,
        CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(RelationalStrings.MissingResultSetWhenSaving);
        }

        plan.Command.PropagateResults(reader);
    }

    private static DbUpdateException CreateDbUpdateException(
        Exception exception,
        IReadOnlyList<IReadOnlyModificationCommand> modificationCommands,
        IDuckDBEngineCapabilities capabilities)
    {
        var command = modificationCommands[0];
        var dualRolePlan = DuckDBDualRoleUpdatePlanner.Create(command, capabilities);
        var message = dualRolePlan.HasChangedForeignKeyWrites && IsInboundReferenceConstraintFailure(exception)
            ? DuckDBCapabilityErrorMessages.UnsupportedDualRoleForeignKeyUpdate(
                command.TableName,
                dualRolePlan.ChangedColumnNames)
            : RelationalStrings.UpdateStoreException;

        return new DbUpdateException(
            message,
            exception,
            modificationCommands.SelectMany(current => current.Entries).ToList());
    }

    internal static DbUpdateException CreateConditionalForeignKeyException(
        DbUpdateException exception,
        IReadOnlyList<IReadOnlyModificationCommand> modificationCommands,
        IDuckDBEngineCapabilities capabilities)
    {
        var command = modificationCommands[0];
        var plan = DuckDBDualRoleUpdatePlanner.Create(command, capabilities);
        return new DbUpdateException(
            DuckDBCapabilityErrorMessages.UnsupportedDualRoleForeignKeyUpdate(
                command.TableName,
                plan.UnchangedForeignKeyWrites.Select(modification => modification.ColumnName)),
            exception.InnerException ?? exception,
            modificationCommands.SelectMany(current => current.Entries).ToList());
    }

    internal static bool IsInboundReferenceConstraintFailure(Exception exception)
        => exception.ToString().Contains("still referenced", StringComparison.OrdinalIgnoreCase);
}