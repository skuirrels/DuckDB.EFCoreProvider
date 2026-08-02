using DuckDB.EFCoreProvider.Extensions.Internal;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>Describes one immutable parameter snapshot in a provider-generated DuckDB command.</summary>
public sealed record DuckDBCommandPlanParameter
{
    private readonly object? _value;

    /// <summary>Creates a command-parameter snapshot.</summary>
    public DuckDBCommandPlanParameter(
        string name,
        Type clrType,
        Type providerParameterType,
        DbType dbType,
        bool isNullable,
        object? value,
        string? storeType = null,
        string? typeMapping = null,
        ParameterDirection direction = ParameterDirection.Input,
        int size = 0,
        byte precision = 0,
        byte scale = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(clrType);
        ArgumentNullException.ThrowIfNull(providerParameterType);

        Name = name;
        ClrType = clrType;
        ProviderParameterType = providerParameterType;
        DbType = dbType;
        IsNullable = isNullable;
        _value = DuckDBCommandValueSnapshot.Create(value);
        StoreType = storeType;
        TypeMapping = typeMapping;
        Direction = direction;
        Size = size;
        Precision = precision;
        Scale = scale;
    }

    /// <summary>Gets the provider parameter name without a SQL placeholder prefix.</summary>
    public string Name { get; }

    /// <summary>Gets the CLR type configured by the resolved EF type mapping.</summary>
    public Type ClrType { get; }

    /// <summary>Gets the concrete ADO.NET parameter type created by the provider.</summary>
    public Type ProviderParameterType { get; }

    /// <summary>Gets the ADO.NET type used to bind the value.</summary>
    public DbType DbType { get; }

    /// <summary>Gets whether the generated parameter accepts database null.</summary>
    public bool IsNullable { get; }

    /// <summary>Gets an owned copy of the captured provider value.</summary>
    public object? Value => DuckDBCommandValueSnapshot.Create(_value);

    /// <summary>Gets the resolved DuckDB store type when it can be recovered from the generated parameter.</summary>
    public string? StoreType { get; }

    /// <summary>Gets the provider type-mapping implementation name when it can be resolved.</summary>
    public string? TypeMapping { get; }

    /// <summary>Gets the parameter direction.</summary>
    public ParameterDirection Direction { get; }

    /// <summary>Gets the configured parameter size.</summary>
    public int Size { get; }

    /// <summary>Gets the configured numeric precision.</summary>
    public byte Precision { get; }

    /// <summary>Gets the configured numeric scale.</summary>
    public byte Scale { get; }

    internal DbParameter CreateDbParameter(DbCommand command)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = Name;
        parameter.Value = DuckDBCommandValueSnapshot.Create(_value) ?? DBNull.Value;
        parameter.DbType = DbType;
        parameter.IsNullable = IsNullable;
        parameter.Direction = Direction;
        parameter.Size = Size;
        parameter.Precision = Precision;
        parameter.Scale = Scale;
        return parameter;
    }
}

/// <summary>
///     Owns a snapshot of one executable provider-generated DuckDB command.
/// </summary>
/// <remarks>
///     This is a database-command contract, not a DuckDB optimizer plan and not a representation of EF's
///     client-side result shaper. Replaying a terminal aggregate exposes the database result directly; for example,
///     an empty <c>Min</c>, <c>Max</c>, or <c>Average</c> command can return database null where EF's non-nullable
///     terminal operator would apply client-side empty-sequence semantics. Multi-command query shapes are rejected.
/// </remarks>
public sealed class DuckDBCommandPlan
{
    /// <summary>Creates an immutable command snapshot.</summary>
    public DuckDBCommandPlan(
        string commandText,
        IEnumerable<DuckDBCommandPlanParameter> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);
        ArgumentNullException.ThrowIfNull(parameters);

        CommandText = commandText;
        Parameters = new ReadOnlyCollection<DuckDBCommandPlanParameter>(parameters.ToArray());
    }

    /// <summary>Gets the exact provider-generated command text.</summary>
    public string CommandText { get; }

    /// <summary>Gets the captured parameters in command order.</summary>
    public IReadOnlyList<DuckDBCommandPlanParameter> Parameters { get; }

}