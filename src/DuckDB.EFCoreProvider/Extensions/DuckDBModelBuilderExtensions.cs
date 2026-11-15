using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     DuckDB-specific extension methods for <see cref="ModelBuilder" />.
/// </summary>
public static class DuckDBModelBuilderExtensions
{
    /// <summary>
    ///     Generates every integer key that would otherwise be database-generated, or left for the application to
    ///     assign on DuckLake, on the client with the Hi-Lo pattern.
    /// </summary>
    /// <remarks>
    ///     Keys configured explicitly, for example with <c>UseAutoIncrement()</c>, <c>ValueGeneratedNever()</c>, or a
    ///     default value, keep their configuration. See
    ///     <see cref="DuckDBPropertyBuilderExtensions.UseHiLo(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder, string?, int?)" />
    ///     for how blocks are reserved.
    /// </remarks>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="tableName">The counter table, or <see langword="null" /> for <c>__EFHiLo</c>.</param>
    /// <param name="blockSize">The values reserved per block, or <see langword="null" /> for 100.</param>
    /// <returns>The same builder so that multiple calls can be chained.</returns>
    public static ModelBuilder UseHiLo(this ModelBuilder modelBuilder, string? tableName = null, int? blockSize = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        DuckDBHiLoConfiguration.Validate(tableName, blockSize);
        var model = modelBuilder.Model;
        model.SetAnnotation(DuckDBAnnotationNames.ValueGenerationStrategy, DuckDBValueGenerationStrategy.HiLo);
        model.SetOrRemoveAnnotation(DuckDBAnnotationNames.HiLoTableName, tableName);
        model.SetOrRemoveAnnotation(DuckDBAnnotationNames.HiLoBlockSize, blockSize);
        return modelBuilder;
    }
}