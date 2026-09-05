using pengdows.crud.fakeDb;
using System.Data;
using System.Data.Common;

namespace Microsoft.EntityFrameworkCore.TestUtilities;

internal sealed record CapturedParameter(string Name, object? Value, Type ParameterType);

internal sealed record CapturedCommand(string CommandText, IReadOnlyList<CapturedParameter> Parameters);

internal sealed class CapturingProviderFactory(CapturingFakeDbConnection connection) : DbProviderFactory
{
    public int CreateConnectionCount { get; private set; }

    public override DbConnection CreateConnection()
    {
        CreateConnectionCount++;
        return connection;
    }
}

internal sealed class CapturingFakeDbConnection : fakeDbConnection
{
    public CapturingFakeDbConnection()
    {
        ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite";
    }

    public List<CapturedCommand> ExecutedReaderCommands { get; } = [];

    public List<CapturedCommand> ExecutedNonQueryCommands { get; } = [];

    public List<CapturedCommand> ExecutedScalarCommands { get; } = [];

    protected override DbCommand CreateDbCommand()
        => new CapturingFakeDbCommand(this);

    private sealed class CapturingFakeDbCommand(CapturingFakeDbConnection connection) : fakeDbCommand(connection)
    {
        public override int ExecuteNonQuery()
        {
            connection.ExecutedNonQueryCommands.Add(Capture());
            return base.ExecuteNonQuery();
        }

        public override object? ExecuteScalar()
        {
            connection.ExecutedScalarCommands.Add(Capture());
            return base.ExecuteScalar();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            connection.ExecutedReaderCommands.Add(Capture());
            return base.ExecuteDbDataReader(behavior);
        }

        private CapturedCommand Capture()
            => new(
                CommandText,
                Parameters.Cast<DbParameter>()
                    .Select(parameter => new CapturedParameter(
                        parameter.ParameterName,
                        parameter.Value,
                        parameter.GetType()))
                    .ToList());
    }
}