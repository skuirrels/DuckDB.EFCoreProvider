using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DuckDB.EFCoreProvider.Metadata.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBAnnotationProvider : RelationalAnnotationProvider
{
    public DuckDBAnnotationProvider(RelationalAnnotationProviderDependencies dependencies) : base(dependencies)
    {
    }

    public override IEnumerable<IAnnotation> For(IColumn column, bool designTime)
    {
        if (!designTime)
        {
            yield break;
        }

        var property = column.PropertyMappings
            .Select(m => m.Property)
            .FirstOrDefault(p => p.GetValueGenerationStrategy() != DuckDBValueGenerationStrategy.None);

        if (property != null)
        {
            var strategy = property.GetValueGenerationStrategy();
            if (strategy != DuckDBValueGenerationStrategy.None)
            {
                yield return new Annotation(DuckDBAnnotationNames.ValueGenerationStrategy, strategy);
            }
        }

                // Surface the DuckDB:StructField annotation (set by DuckDBStructFieldConvention on
                // scalar sub-properties of struct-mapped complex properties) so the DDL and write
                // pipelines can group sub-property columns into single STRUCT columns.
                var structFieldAnnotation = column.PropertyMappings
                    .Select(m => m.Property.FindAnnotation(DuckDBAnnotationNames.StructField))
                    .FirstOrDefault(a => a is not null);

                if (structFieldAnnotation?.Value is DuckDBStructFieldInfo structFieldInfo)
                {
                    yield return new Annotation(DuckDBAnnotationNames.StructField, structFieldInfo);
                }
            }
        }
