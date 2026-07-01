using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuckDB.EFCoreProvider.Extensions;

public static class DuckDBPropertyBuilderExtensions
{
    public static PropertyBuilder UseAutoIncrement(this PropertyBuilder propertyBuilder)
    {
        propertyBuilder.ValueGeneratedOnAdd();
        propertyBuilder.Metadata.SetValueGenerationStrategy(DuckDBValueGenerationStrategy.AutoIncrement);

        return propertyBuilder;
    }

    public static PropertyBuilder<TProperty> UseAutoIncrement<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder)
        => (PropertyBuilder<TProperty>)UseAutoIncrement((PropertyBuilder)propertyBuilder);

    public static ColumnBuilder UseAutoIncrement(
        this ColumnBuilder columnBuilder)
    {
        columnBuilder.Overrides.SetValueGenerationStrategy(DuckDBValueGenerationStrategy.AutoIncrement);

        return columnBuilder;
    }
}
