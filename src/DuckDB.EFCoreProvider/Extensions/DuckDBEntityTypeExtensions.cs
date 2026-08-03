using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     Entity type extension methods for DuckDB-specific metadata.
/// </summary>
public static class DuckDBEntityTypeExtensions
{
    /// <summary>
    ///     Gets the DuckDB file-reading table function configured for the specified entity type
    ///     (e.g. <c>read_parquet</c>, <c>read_csv</c>, <c>read_json</c>), or <see langword="null" /> if the
    ///     entity type is not file-backed.
    /// </summary>
    public static string? GetDuckDBFileSourceFunction(this IEntityType entityType)
        => entityType.FindAnnotation(DuckDBAnnotationNames.FileSourceFunction)?.Value as string;

    /// <summary>
    ///     Gets the file path or glob pattern configured for the specified entity type, or
    ///     <see langword="null" /> if the entity type is not file-backed.
    /// </summary>
    public static string? GetDuckDBFileSourcePath(this IEntityType entityType)
        => entityType.FindAnnotation(DuckDBAnnotationNames.FileSourcePath)?.Value as string;

    /// <summary>
    ///     Gets the parquet path configured for the specified entity type.
    /// </summary>
    /// <returns>The configured parquet path, or <see langword="null" /> if the entity type is not parquet-backed.</returns>
    public static string? GetParquetPath(this IEntityType entityType)
        => entityType.GetDuckDBFileSourceFunction() == "read_parquet"
            ? entityType.GetDuckDBFileSourcePath()
            : null;

    /// <summary>
    ///     Configures the entity type to query data from the specified parquet path via DuckDB
    ///     <c>read_parquet(...)</c>. Affects query SQL generation only; it does not configure the update pipeline.
    /// </summary>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="path">The parquet file path or glob pattern.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder<TEntity> FromParquet<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        string path)
        where TEntity : class
        => entityTypeBuilder.FromDuckDBFile("read_parquet", path);

    /// <summary>
    ///     Maps a scalar entity property to a STRUCT leaf selected by a typed CLR member path.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being configured.</typeparam>
    /// <typeparam name="TProperty">The scalar property type.</typeparam>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="propertyExpression">The scalar EF property that stores the foreign-key value.</param>
    /// <param name="fieldExpression">
    ///     A nested member path whose first member identifies the STRUCT root and whose last member identifies the
    ///     leaf, for example <c>order =&gt; order.Relationship.ParentId</c>.
    /// </param>
    /// <param name="structColumnName">
    ///     An optional physical STRUCT root override. When omitted, the first member name is used.
    /// </param>
    /// <param name="leafFieldName">
    ///     An optional physical leaf override. When omitted, the last member name is converted to camel case.
    /// </param>
    /// <remarks>
    ///     The field selector describes the physical STRUCT path; the first selector remains the scalar EF property
    ///     used by the relationship. It does not make a nested complex property an EF foreign key.
    ///     Use <see cref="DuckDBStructPropertyBuilderExtensions.HasStructField{TProperty}(PropertyBuilder{TProperty}, string, string[])" />
    ///     when intermediate physical names do not follow the CLR path.
    /// </remarks>
    public static EntityTypeBuilder<TEntity> HasStructFieldPath<TEntity, TProperty>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        Expression<Func<TEntity, TProperty>> propertyExpression,
        Expression<Func<TEntity, object?>> fieldExpression,
        string? structColumnName = null,
        string? leafFieldName = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entityTypeBuilder);

        DuckDBStructPropertyBuilderExtensions.ConfigureStructFieldPath(
            entityTypeBuilder.Property(propertyExpression),
            fieldExpression,
            structColumnName,
            leafFieldName);

        return entityTypeBuilder;
    }

    /// <summary>
    ///     Configures the entity type to query data from the specified CSV path via DuckDB <c>read_csv(...)</c>
    ///     (DuckDB auto-detects dialect and column types). Affects query SQL generation only.
    /// </summary>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="path">The CSV file path or glob pattern.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder<TEntity> FromCsv<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        string path)
        where TEntity : class
        => entityTypeBuilder.FromDuckDBFile("read_csv", path);

    /// <summary>
    ///     Configures the entity type to query data from the specified JSON path via DuckDB <c>read_json(...)</c>
    ///     (DuckDB auto-detects structure and column types). Affects query SQL generation only.
    /// </summary>
    /// <param name="entityTypeBuilder">The builder for the entity type being configured.</param>
    /// <param name="path">The JSON file path or glob pattern.</param>
    /// <returns>The same builder instance so that multiple calls can be chained.</returns>
    public static EntityTypeBuilder<TEntity> FromJsonFile<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        string path)
        where TEntity : class
        => entityTypeBuilder.FromDuckDBFile("read_json", path);

    private static EntityTypeBuilder<TEntity> FromDuckDBFile<TEntity>(
        this EntityTypeBuilder<TEntity> entityTypeBuilder,
        string function,
        string path)
        where TEntity : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        entityTypeBuilder.Metadata.SetAnnotation(DuckDBAnnotationNames.FileSourceFunction, function);
        entityTypeBuilder.Metadata.SetAnnotation(DuckDBAnnotationNames.FileSourcePath, path);

        return entityTypeBuilder;
    }
}