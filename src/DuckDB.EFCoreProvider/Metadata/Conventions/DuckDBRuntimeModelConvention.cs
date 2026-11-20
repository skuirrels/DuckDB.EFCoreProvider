using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace DuckDB.EFCoreProvider.Metadata.Conventions;

/// <summary>
///     A convention that creates an optimised copy of the mutable model.
/// </summary>
/// <remarks>
///     See <see href="https://aka.ms/efcore-docs-conventions">Model building conventions</see>.
/// </remarks>
public class DuckDBRuntimeModelConvention : RelationalRuntimeModelConvention
{
    /// <summary>
    ///     Creates a new instance of <see cref="DuckDBRuntimeModelConvention" />.
    /// </summary>
    /// <param name="dependencies">Parameter object containing dependencies for this convention.</param>
    /// <param name="relationalDependencies">Parameter object containing relational dependencies for this convention.</param>
    public DuckDBRuntimeModelConvention(ProviderConventionSetBuilderDependencies dependencies, RelationalConventionSetBuilderDependencies relationalDependencies) : base(dependencies, relationalDependencies)
    {
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Keeps a property's collation under a provider annotation, because the base convention drops
    ///     <c>Relational:Collation</c> and <c>UseCaseInsensitiveStringComparisons()</c> must leave collated columns alone.
    /// </remarks>
    protected override void ProcessPropertyAnnotations(
        Dictionary<string, object?> annotations,
        IProperty property,
        RuntimeProperty runtimeProperty,
        bool runtime)
    {
        base.ProcessPropertyAnnotations(annotations, property, runtimeProperty, runtime);

        if (!runtime && property.GetCollation() is { } collation)
        {
            annotations[DuckDBAnnotationNames.RuntimeCollation] = collation;
        }
    }
}
