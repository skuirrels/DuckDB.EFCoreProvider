using System.Data;
using System.Data.Common;
using pengdows.crud.fakeDb;

namespace DuckDB.EFCoreProvider.Tests.FakeDb;

public sealed record CapturedParameter(string Name, object? Value);

public sealed record CapturedCommand(string CommandText, IReadOnlyList<CapturedParameter> Parameters);

/// <summary>
///     The public pengdows.crud.fakeDb 2.0.5 release does not snapshot a command's bound parameter
///     values at execution time (a later, not-yet-public release added that) -- EF Core clears a
///     command's Parameters after executing it, so by the time a test can inspect
///     <see cref="fakeDbConnection.CreatedCommands" /> the values are already gone. This thin
///     subclass captures each command's text and parameter values the moment it executes, which is
///     all these tests need: proof that a real value reached the fake connection's command, not just
///     that no exception was thrown.
/// </summary>
internal sealed class CapturingFakeDbConnection : fakeDbConnection
{
    public List<CapturedCommand> ExecutedReaderCommands { get; } = [];

    public List<CapturedCommand> ExecutedNonQueryCommands { get; } = [];

    protected override DbCommand CreateDbCommand() => new CapturingFakeDbCommand(this);

    private sealed class CapturingFakeDbCommand(CapturingFakeDbConnection connection) : fakeDbCommand(connection)
    {
        public override int ExecuteNonQuery()
        {
            connection.ExecutedNonQueryCommands.Add(Capture());
            return base.ExecuteNonQuery();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            connection.ExecutedReaderCommands.Add(Capture());
            return base.ExecuteDbDataReader(behavior);
        }

        private CapturedCommand Capture()
            => new(
                CommandText,
                Parameters.Cast<DbParameter>().Select(p => new CapturedParameter(p.ParameterName, p.Value)).ToList());
    }
}
