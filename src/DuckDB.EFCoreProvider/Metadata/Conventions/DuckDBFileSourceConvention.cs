using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Metadata.Conventions;

/// <summary>
///     A convention that applies file-source metadata from a <see cref="DuckDBFileSourceAttribute" />
///     (e.g. <see cref="FromParquetAttribute" />, <see cref="FromCsvAttribute" />,
///     <see cref="FromJsonFileAttribute" />).
/// </summary>
/// <remarks>
///     When an entity type is added to the model, this convention reads the file-source attribute from the
///     CLR type and stores the configured DuckDB table function and path in the corresponding annotations.
/// </remarks>
public sealed class DuckDBFileSourceConvention : IEntityTypeAddedConvention
{
    /// <inheritdoc />
    public void ProcessEntityTypeAdded(
        IConventionEntityTypeBuilder entityTypeBuilder,
        IConventionContext<IConventionEntityTypeBuilder> context)
    {
        var attribute = entityTypeBuilder.Metadata.ClrType?.GetCustomAttribute<DuckDBFileSourceAttribute>(inherit: true);

        if (attribute is null)
        {
            return;
        }

        entityTypeBuilder.HasAnnotation(DuckDBAnnotationNames.FileSourceFunction, attribute.Function);
        entityTypeBuilder.HasAnnotation(DuckDBAnnotationNames.FileSourcePath, attribute.Path);
    }
}
