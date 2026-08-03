using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Extensions.Internal;

internal sealed class DuckDBDynamicCommandExecutor(
    ICurrentDbContext currentDbContext,
    IRelationalDatabaseFacadeDependencies dependencies,
    IConcurrencyDetector concurrencyDetector,
    IRelationalCommandBuilderFactory commandBuilderFactory)
{
    public Task<DuckDBDynamicQueryResult> ExecuteRawAsync(
        string sql,
        IReadOnlyList<object?> parameters,
        CancellationToken cancellationToken)
    {
        RawSqlCommand command;
        if (parameters.Count == 0)
        {
            command = new RawSqlCommand(
                dependencies.RawSqlCommandBuilder.Build(sql),
                new Dictionary<string, object?>());
        }
        else
        {
            command = dependencies.RawSqlCommandBuilder.Build(
                sql,
                parameters,
                currentDbContext.Context.Model);
        }

        return ExecuteAsync(command, cancellationToken);
    }

    public Task<DuckDBDynamicQueryResult> ExecuteNamedAsync(
        string sql,
        IReadOnlyList<DbParameter> parameters,
        CancellationToken cancellationToken)
    {
        var commandBuilder = commandBuilderFactory.Create();
        var names = new HashSet<string>(StringComparer.Ordinal);

        using var parameterFactoryCommand = dependencies.RelationalConnection.DbConnection.CreateCommand();
        foreach (var source in parameters)
        {
            ArgumentNullException.ThrowIfNull(source);
            var name = source.ParameterName.RemoveDollarSign();
            ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(parameters));

            if (!names.Add(name))
            {
                throw new ArgumentException(
                    $"The named parameter '{name}' was supplied more than once.",
                    nameof(parameters));
            }

            if (source.Direction is not 0 and not System.Data.ParameterDirection.Input)
            {
                throw new NotSupportedException("Dynamic query parameters must use Input direction.");
            }

            var parameter = CloneParameter(source, parameterFactoryCommand, name);
            commandBuilder.AddRawParameter(name, parameter);
        }

        var command = new RawSqlCommand(
            commandBuilder.Append(sql).Build(),
            new Dictionary<string, object?>());
        return ExecuteAsync(command, cancellationToken);
    }

    public Task<DuckDBDynamicQueryResult> ExecutePlanAsync(
        DuckDBCommandPlan plan,
        CancellationToken cancellationToken)
    {
        using var parameterFactoryCommand = dependencies.RelationalConnection.DbConnection.CreateCommand();
        var parameters = plan.Parameters
            .Select(parameter => parameter.CreateDbParameter(parameterFactoryCommand))
            .ToArray();
        return ExecuteNamedAsync(plan.CommandText, parameters, cancellationToken);
    }

    private async Task<DuckDBDynamicQueryResult> ExecuteAsync(
        RawSqlCommand command,
        CancellationToken cancellationToken)
    {
        using var criticalSection = concurrencyDetector.EnterCriticalSection();
        var parameterObject = new RelationalCommandParameterObject(
            dependencies.RelationalConnection,
            command.ParameterValues,
            readerColumns: null,
            currentDbContext.Context,
            dependencies.CommandLogger,
            CommandSource.FromSqlQuery);

        var reader = await command.RelationalCommand
            .ExecuteReaderAsync(parameterObject, cancellationToken)
            .ConfigureAwait(false);

        return new DuckDBDynamicQueryResult(reader, concurrencyDetector);
    }

    private static DbParameter CloneParameter(DbParameter source, DbCommand command, string name)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = source.Value ?? DBNull.Value;
        parameter.DbType = source.DbType;
        parameter.Direction = System.Data.ParameterDirection.Input;
        parameter.IsNullable = source.IsNullable;
        parameter.Size = source.Size;
        parameter.Precision = source.Precision;
        parameter.Scale = source.Scale;
        parameter.SourceColumn = source.SourceColumn;
        parameter.SourceColumnNullMapping = source.SourceColumnNullMapping;
        return parameter;
    }
}