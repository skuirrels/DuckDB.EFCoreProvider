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
            // Prefer the entity-level column map when available: it holds the correct
            // DuckDBStructFieldInfo for shared complex types used in multiple struct
            // columns (e.g. Billing.City vs Shipping.City). Fall back to the legacy leaf
            // property annotation for explicit HasStructField or older conventions.
            DuckDBStructFieldInfo? ResolveStructFieldInfo()
            {
                var columnMap = column.Table?.EntityTypeMappings
                        .Select(e => e.TypeBase is IEntityType entityType ? entityType.GetStructColumnMap() : null)
                    .FirstOrDefault(m => m is not null && m.ContainsKey(column.Name));

                if (columnMap?.TryGetValue(column.Name, out var info) == true)
                {
                    return info;
                }

                return column.PropertyMappings
                    .Select(m => m.Property.FindAnnotation(DuckDBAnnotationNames.StructField))
                    .FirstOrDefault(a => a is not null)?.Value as DuckDBStructFieldInfo;
            }

            var structFieldInfo = ResolveStructFieldInfo();
            if (structFieldInfo is not null)
            {
                yield return new Annotation(DuckDBAnnotationNames.StructField, structFieldInfo);
            }

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
        }
    }
