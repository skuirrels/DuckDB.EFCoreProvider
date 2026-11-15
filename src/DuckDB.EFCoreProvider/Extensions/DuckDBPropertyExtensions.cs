using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     DuckDB specific extension methods for <see cref="IProperty" />.
/// </summary>
public static class DuckDBPropertyExtensions
{
    /// <summary>
    ///     Returns the <see cref="DuckDBStructFieldInfo" /> stored on this property via
    ///     <c>HasStructField</c>, or <see langword="null" /> when the property is not
    ///     mapped to a DuckDB STRUCT sub-field.
    /// </summary>
    public static DuckDBStructFieldInfo? GetStructFieldInfo(this IReadOnlyProperty property)
        => property.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value as DuckDBStructFieldInfo;
    /// <summary>
    ///     Retrieves the <see cref="DuckDBValueGenerationStrategy"/> associated with the specified property.
    /// </summary>
    /// <param name="property">
    ///     The property for which the value generation strategy is being retrieved.
    /// </param>
    /// <returns>
    ///     The <see cref="DuckDBValueGenerationStrategy"/> if it is explicitly defined for the property;
    ///     otherwise, <see cref="DuckDBValueGenerationStrategy.None"/>.
    /// </returns>
    public static DuckDBValueGenerationStrategy GetValueGenerationStrategy(this IReadOnlyProperty property)
    {
        var annotation = property.FindAnnotation(DuckDBAnnotationNames.ValueGenerationStrategy);
        if (annotation?.Value != null)
        {
            return (DuckDBValueGenerationStrategy)annotation.Value;
        }

        return DuckDBValueGenerationStrategy.None;
    }

    /// <summary>
    ///     Sets the <see cref="DuckDBValueGenerationStrategy"/> for the specified property.
    /// </summary>
    /// <param name="property">
    ///     The property for which the value generation strategy is being set.
    /// </param>
    /// <param name="value">
    ///     The <see cref="DuckDBValueGenerationStrategy"/> value to apply to the property, or <c>null</c> to remove the annotation.
    /// </param>
    public static void SetValueGenerationStrategy(
        this IMutableProperty property,
        DuckDBValueGenerationStrategy? value)
    {
        property.SetOrRemoveAnnotation(DuckDBAnnotationNames.ValueGenerationStrategy, value);
    }

    /// <summary>
    ///     Sets the specified <see cref="DuckDBValueGenerationStrategy"/> for the given mutable property.
    /// </summary>
    /// <param name="property">
    ///     The mutable property for which the value generation strategy is being set.
    /// </param>
    /// <param name="value">
    ///     The <see cref="DuckDBValueGenerationStrategy"/> to set, or <c>null</c> to remove the strategy.
    /// </param>
    /// <param name="fromDataAnnotation">
    ///     <see langword="true" /> if the configuration was specified using a data annotation.
    /// </param>
    public static DuckDBValueGenerationStrategy? SetValueGenerationStrategy(
        this IConventionProperty property,
        DuckDBValueGenerationStrategy? value,
        bool fromDataAnnotation = false)
    {
        property.SetOrRemoveAnnotation(DuckDBAnnotationNames.ValueGenerationStrategy, value, fromDataAnnotation);
        return value;
    }

    /// <summary>
    ///     The default counter table for Hi-Lo key generation.
    /// </summary>
    public const string DefaultHiLoTableName = "__EFHiLo";

    /// <summary>
    ///     The default number of key values reserved per Hi-Lo block.
    /// </summary>
    public const int DefaultHiLoBlockSize = 100;

    /// <summary>
    ///     Returns the counter table that reserves Hi-Lo key blocks for this property, falling back to the model
    ///     default and then <see cref="DefaultHiLoTableName" />.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns>The counter table name.</returns>
    public static string GetHiLoTableName(this IReadOnlyProperty property)
        => property[DuckDBAnnotationNames.HiLoTableName] as string
            ?? property.DeclaringType.Model[DuckDBAnnotationNames.HiLoTableName] as string
            ?? DefaultHiLoTableName;

    /// <summary>
    ///     Returns the number of key values each Hi-Lo block reserves for this property, falling back to the model
    ///     default and then <see cref="DefaultHiLoBlockSize" />.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns>The block size.</returns>
    public static int GetHiLoBlockSize(this IReadOnlyProperty property)
        => property[DuckDBAnnotationNames.HiLoBlockSize] as int?
            ?? property.DeclaringType.Model[DuckDBAnnotationNames.HiLoBlockSize] as int?
            ?? DefaultHiLoBlockSize;

    /// <summary>
    ///     Sets the counter table that reserves Hi-Lo key blocks for this property.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="tableName">The counter table name, or <see langword="null" /> to use the default.</param>
    public static void SetHiLoTableName(this IMutableProperty property, string? tableName)
        => property.SetOrRemoveAnnotation(DuckDBAnnotationNames.HiLoTableName, tableName);

    /// <summary>
    ///     Sets the number of key values each Hi-Lo block reserves for this property.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="blockSize">The block size, or <see langword="null" /> to use the default.</param>
    public static void SetHiLoBlockSize(this IMutableProperty property, int? blockSize)
        => property.SetOrRemoveAnnotation(DuckDBAnnotationNames.HiLoBlockSize, blockSize);

    public static void SetValueGenerationStrategy(
        this IMutableRelationalPropertyOverrides overrides,
        DuckDBValueGenerationStrategy? value)
        => overrides.SetOrRemoveAnnotation(DuckDBAnnotationNames.ValueGenerationStrategy, value);
}