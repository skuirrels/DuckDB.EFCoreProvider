using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DuckDB.EFCoreProvider.Infrastructure.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBModelValidator : RelationalModelValidator
{
    public DuckDBModelValidator(ModelValidatorDependencies dependencies, RelationalModelValidatorDependencies relationalDependencies) : base(dependencies, relationalDependencies)
    {
    }

    /// <inheritdoc />
    public override void Validate(IModel model, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        base.Validate(model, logger);

        ValidateAutoIncrement(model);
    }

    private static void ValidateAutoIncrement(IModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var property in entityType.GetDeclaredProperties())
            {
                if (property.GetValueGenerationStrategy() != DuckDBValueGenerationStrategy.AutoIncrement)
                {
                    continue;
                }

                var clrType = property.ClrType.UnwrapNullableType();
                if (!DuckDBValueGenerationStrategyCompatibility.IsAutoIncrementCompatible(clrType))
                {
                    throw new InvalidOperationException(
                        $"DuckDB auto-increment value generation can only be configured for integer properties. Property '{entityType.DisplayName()}.{property.Name}' is '{property.ClrType.Name}'.");
                }

                if (property.GetTypeMapping().Converter != null)
                {
                    throw new InvalidOperationException(
                        $"DuckDB auto-increment value generation cannot be configured for property '{entityType.DisplayName()}.{property.Name}' because it has a value converter.");
                }
            }
        }
    }
}
