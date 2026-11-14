using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Globalization;

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

    internal DuckDBStoreValueWriter(IProperty property)
    {
        Property = property;
        _setter = ((IRuntimePropertyBase)property).GetSetter();
        _converter = property.GetTypeMapping().Converter;
        var providerClrType = _converter?.ProviderClrType ?? property.ClrType;
        _providerClrType = Nullable.GetUnderlyingType(providerClrType) ?? providerClrType;
    }

    internal IProperty Property { get; }

    internal void Write(object entity, object? providerValue)
        => _setter.SetClrValueUsingContainingEntity(entity, ToClrValue(providerValue));

    private object? ToClrValue(object? value)
    {
        if (value is null or DBNull)
        {
            return _converter?.ConvertFromProvider(null);
        }

        var providerValue = value.GetType() == _providerClrType || value is not IConvertible
            ? value
            : Convert.ChangeType(value, _providerClrType, CultureInfo.InvariantCulture);

        return _converter is null ? providerValue : _converter.ConvertFromProvider(providerValue);
    }
}