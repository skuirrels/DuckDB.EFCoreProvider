using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DuckDB.EFCoreProvider.Migrations.Internal;

internal sealed class DuckDBMigrationsAnnotationProvider(
    MigrationsAnnotationProviderDependencies dependencies)
    : MigrationsAnnotationProvider(dependencies)
{
    public override IEnumerable<IAnnotation> ForRemove(IColumn column)
        => GetStructAnnotations(column);

    public override IEnumerable<IAnnotation> ForRename(IColumn column)
        => GetStructAnnotations(column);

    public override IEnumerable<IAnnotation> ForRemove(IForeignKeyConstraint foreignKey)
    {
        if (DuckDBStructRelationalMetadata.IsStructFieldForeignKey(foreignKey))
        {
            yield return new Annotation(DuckDBAnnotationNames.LogicalStructForeignKey, true);
        }
    }

    private static IEnumerable<IAnnotation> GetStructAnnotations(IColumn column)
    {
        if (DuckDBStructRelationalMetadata.FindFieldInfo(column) is { } field)
        {
            yield return new Annotation(DuckDBAnnotationNames.StructField, field);
        }
    }
}