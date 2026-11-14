using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Extensions.Internal;

/// <summary>
///     Writes a value read from DuckDB to an entity property, converting it from the provider type through the
///     property's value converter, as EF does when it reads a store-generated value back after <c>SaveChanges</c>.
/// </summary>
internal sealed class DuckDBStoreValueWriter
{
    private readonly IClrPropertySetter _setter;
    private readonly ValueConverter? _converter;
    private readonly Type _providerClrType;
    private readonly Func<DbDataReader, int, object> _readProviderValue;

    internal DuckDBStoreValueWriter(IProperty property)
    {
        Property = property;
        _setter = ((IRuntimePropertyBase)property).GetSetter();
        var typeMapping = property.GetRelationalTypeMapping();
        _converter = typeMapping.Converter;
        var providerClrType = _converter?.ProviderClrType ?? property.ClrType;
        _providerClrType = Nullable.GetUnderlyingType(providerClrType) ?? providerClrType;
        _readProviderValue = CompileProviderValueReader(typeMapping, _providerClrType);
    }

    internal IProperty Property { get; }

    /// <summary>
    ///     Writes a value that was not read from the property's own mapped column, such as a sequence value, whose
    ///     store type can differ from the column's.
    /// </summary>
    internal void Write(object entity, object? providerValue)
        => _setter.SetClrValueUsingContainingEntity(entity, ToClrValue(providerValue));

    /// <summary>
    ///     Writes the value of the property's mapped column, read through the type mapping exactly as EF
    ///     materializes it, so store types whose reader value differs from the CLR type (for example TIMESTAMPTZ)
    ///     round-trip.
    /// </summary>
    internal void WriteColumn(object entity, DbDataReader reader, int ordinal)
        => _setter.SetClrValueUsingContainingEntity(
            entity,
            reader.IsDBNull(ordinal)
                ? _converter?.ConvertFromProvider(null)
                : FromProvider(_readProviderValue(reader, ordinal)));

    private object? ToClrValue(object? value)
    {
        if (value is null or DBNull)
        {
            return _converter?.ConvertFromProvider(null);
        }

        return FromProvider(
            value.GetType() == _providerClrType || value is not IConvertible
                ? value
                : Convert.ChangeType(value, _providerClrType, CultureInfo.InvariantCulture));
    }

    private object? FromProvider(object providerValue)
        => _converter is null ? providerValue : _converter.ConvertFromProvider(providerValue);

    private static Func<DbDataReader, int, object> CompileProviderValueReader(
        Microsoft.EntityFrameworkCore.Storage.RelationalTypeMapping typeMapping,
        Type providerClrType)
    {
        var reader = Expression.Parameter(typeof(DbDataReader), "reader");
        var ordinal = Expression.Parameter(typeof(int), "ordinal");
        var method = typeMapping.GetDataReaderMethod();
        Expression instance = method.DeclaringType is { } declaringType && declaringType != typeof(DbDataReader)
            ? Expression.Convert(reader, declaringType)
            : reader;
        Expression value = typeMapping.CustomizeDataReaderExpression(Expression.Call(instance, method, ordinal));
        if (value.Type != providerClrType)
        {
            value = Expression.Convert(value, providerClrType);
        }

        return Expression.Lambda<Func<DbDataReader, int, object>>(
            Expression.Convert(value, typeof(object)),
            reader,
            ordinal).Compile();
    }
}