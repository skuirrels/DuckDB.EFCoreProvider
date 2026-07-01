using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.EntityFrameworkCore.ModelBuilding;

public static class DuckDBTestModelBuilderExtensions
{
    public static ModelBuilderTest.TestPropertyBuilder<TProperty> UseAutoIncrement<TProperty>(
        this ModelBuilderTest.TestPropertyBuilder<TProperty> builder)
    {
        switch (builder)
        {
            case IInfrastructure<PropertyBuilder<TProperty>> genericBuilder:
                genericBuilder.Instance.UseAutoIncrement();
                break;
            case IInfrastructure<PropertyBuilder> nonGenericBuilder:
                nonGenericBuilder.Instance.UseAutoIncrement();
                break;
        }

        return builder;
    }
}
