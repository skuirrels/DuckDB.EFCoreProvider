using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Extensions.Internal;

/// <summary>
///     Reserves database-generated key values for one column of a bulk insert and writes them to the entities
///     that do not already carry a key, matching what <c>SaveChanges</c> lets the database generate.
/// </summary>
/// <remarks>
///     The appender writes every column, so it cannot apply a column default itself. The column default is
///     read from DuckDB's catalog and evaluated once per row that needs a value, for example
///     <c>nextval('Orders_Id_seq')</c>. The expression is DuckDB's own stored default for the column, the
///     same expression an <c>INSERT</c> would evaluate, so no model-derived SQL is introduced.
/// </remarks>
internal sealed class DuckDBBulkInsertKeyReservation
{
    private readonly IClrPropertyGetter _getter;
    private readonly DuckDBStoreValueWriter _writer;

    internal DuckDBBulkInsertKeyReservation(IProperty property, string columnDefault)
    {
        Property = property;
        ColumnDefault = columnDefault;
        CommandText = $"SELECT {columnDefault} FROM range($n)";
        _getter = property.GetGetter();
        _writer = new DuckDBStoreValueWriter(property);
    }

    internal IProperty Property { get; }

    internal string ColumnDefault { get; }

    internal string CommandText { get; }

    /// <summary>
    ///     A column is reserved when EF expects the database to generate it on insert and it identifies the row:
    ///     a generated primary key property, or any property configured with <c>UseAutoIncrement()</c>.
    /// </summary>
    internal static bool IsReservable(IProperty property)
        => property.ValueGenerated.HasFlag(ValueGenerated.OnAdd)
            && (property.IsPrimaryKey()
                || property.GetValueGenerationStrategy() == DuckDBValueGenerationStrategy.AutoIncrement);

    /// <summary>
    ///     Assigns reserved values to the rows whose key still holds its sentinel value.
    /// </summary>
    /// <returns>The number of values reserved.</returns>
    internal int Apply<TEntity>(DbConnection connection, TEntity[] rows, int count)
        where TEntity : class
    {
        var pending = 0;
        for (var i = 0; i < count; i++)
        {
            if (_getter.HasSentinelValueUsingContainingEntity(rows[i]))
            {
                pending++;
            }
        }

        if (pending == 0)
        {
            return 0;
        }

        using var command = connection.CreateCommand();
        command.CommandText = CommandText;
        command.AddParameter("n", (long)pending);

        using var reader = command.ExecuteReader();
        var assigned = 0;
        for (var i = 0; i < count && assigned < pending; i++)
        {
            if (!_getter.HasSentinelValueUsingContainingEntity(rows[i]))
            {
                continue;
            }

            if (!reader.Read())
            {
                break;
            }

            var value = reader.GetValue(0);
            if (value is DBNull)
            {
                throw new InvalidOperationException(
                    $"Bulk insert could not reserve a value for column '{Property.GetColumnName()}': "
                    + $"its default '{ColumnDefault}' returned NULL.");
            }

            _writer.Write(rows[i], value);
            assigned++;
        }

        if (assigned != pending)
        {
            throw new InvalidOperationException(
                $"Bulk insert reserved {assigned} of {pending} values for column '{Property.GetColumnName()}' "
                + $"from its default '{ColumnDefault}'.");
        }

        return pending;
    }
}