using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
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

    /// <summary>
    ///     Generates key values on the client with the Hi-Lo pattern: blocks of values are reserved from a
    ///     one-row counter table, so the key is assigned before insert without a sequence or <c>RETURNING</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the way to get generated keys on DuckLake, which has no sequences. It also works on native
    ///         DuckDB. It is not available on Quack, where sequences already generate keys.
    ///     </para>
    ///     <para>
    ///         The counter table is created on first use, starting above the largest existing key of every property
    ///         that shares it. Blocks are reserved on a separate connection and committed immediately, so a rolled
    ///         back transaction leaves a gap rather than reusing values. Properties that share a counter table share
    ///         its values, so keys stay unique across those tables.
    ///     </para>
    /// </remarks>
    /// <param name="propertyBuilder">The builder for the key property.</param>
    /// <param name="tableName">The counter table, or <see langword="null" /> for the model default.</param>
    /// <param name="blockSize">The values reserved per block, or <see langword="null" /> for the model default.</param>
    /// <returns>The same builder so that multiple calls can be chained.</returns>
    public static PropertyBuilder UseHiLo(
        this PropertyBuilder propertyBuilder,
        string? tableName = null,
        int? blockSize = null)
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        DuckDBHiLoConfiguration.Validate(tableName, blockSize);
        propertyBuilder.ValueGeneratedOnAdd();
        propertyBuilder.Metadata.SetValueGenerationStrategy(DuckDBValueGenerationStrategy.HiLo);
        propertyBuilder.Metadata.SetHiLoTableName(tableName);
        propertyBuilder.Metadata.SetHiLoBlockSize(blockSize);
        return propertyBuilder;
    }

    /// <inheritdoc cref="UseHiLo(PropertyBuilder, string?, int?)" />
    public static PropertyBuilder<TProperty> UseHiLo<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder,
        string? tableName = null,
        int? blockSize = null)
        => (PropertyBuilder<TProperty>)UseHiLo((PropertyBuilder)propertyBuilder, tableName, blockSize);

    public static ColumnBuilder UseAutoIncrement(this ColumnBuilder columnBuilder)
    {
        columnBuilder.Overrides.SetValueGenerationStrategy(DuckDBValueGenerationStrategy.AutoIncrement);
        return columnBuilder;
    }

    // Keep the original declaring type available for callers that use the provider
    // extension methods through a static class-qualified call.
    public static ComplexPropertyBuilder UseStructMapping(ComplexPropertyBuilder propertyBuilder)
        => DuckDBStructPropertyBuilderExtensions.UseStructMapping(propertyBuilder);

    public static ComplexPropertyBuilder<TComplex> UseStructMapping<TComplex>(
        ComplexPropertyBuilder<TComplex> propertyBuilder)
        where TComplex : class
        => DuckDBStructPropertyBuilderExtensions.UseStructMapping(propertyBuilder);

    public static PropertyBuilder<TProperty> HasStructField<TProperty>(
        PropertyBuilder<TProperty> propertyBuilder,
        string structColumnName,
        params string[] nestedFieldNames)
        => DuckDBStructPropertyBuilderExtensions.HasStructField(
            propertyBuilder,
            structColumnName,
            nestedFieldNames);

    public static ComplexTypePropertyBuilder<TProperty> HasStructField<TProperty>(
        ComplexTypePropertyBuilder<TProperty> propertyBuilder,
        string structColumnName,
        params string[] nestedFieldNames)
        => DuckDBStructPropertyBuilderExtensions.HasStructField(
            propertyBuilder,
            structColumnName,
            nestedFieldNames);
}