using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace DuckDB.EFCoreProvider.Metadata.Conventions;

/// <summary>
///     A builder for building conventions for DuckDB.
/// </summary>
public class DuckDBConventionSetBuilder : RelationalConventionSetBuilder
{
    private readonly IDuckDBEngineCapabilities _capabilities;

    /// <summary>
    ///     Creates a new instance of <see cref="DuckDBConventionSetBuilder" />.
    /// </summary>
    /// <param name="dependencies">The core dependencies for this service.</param>
    /// <param name="relationalDependencies">The relational dependencies for this service.</param>
    public DuckDBConventionSetBuilder(
        ProviderConventionSetBuilderDependencies dependencies,
        RelationalConventionSetBuilderDependencies relationalDependencies)
        : this(dependencies, relationalDependencies, null, DuckDBEngineCapabilities.Native)
    {
    }

    /// <summary>Creates a convention-set builder with DuckLake backend options.</summary>
    /// <param name="dependencies">The core dependencies for this service.</param>
    /// <param name="relationalDependencies">The relational dependencies for this service.</param>
    /// <param name="duckLakeSingletonOptions">Backend options that affect model conventions.</param>
    public DuckDBConventionSetBuilder(
        ProviderConventionSetBuilderDependencies dependencies,
        RelationalConventionSetBuilderDependencies relationalDependencies,
        IDuckLakeSingletonOptions? duckLakeSingletonOptions)
        : this(
            dependencies,
            relationalDependencies,
            duckLakeSingletonOptions,
            DuckDBEngineCapabilities.FromDuckLakeOptions(duckLakeSingletonOptions))
    {
    }

    /// <summary>Creates a convention-set builder with the configured engine capabilities.</summary>
    /// <param name="dependencies">The core dependencies for this service.</param>
    /// <param name="relationalDependencies">The relational dependencies for this service.</param>
    /// <param name="duckLakeSingletonOptions">Legacy backend options retained for constructor compatibility.</param>
    /// <param name="capabilities">Capabilities that drive provider conventions.</param>
    public DuckDBConventionSetBuilder(
        ProviderConventionSetBuilderDependencies dependencies,
        RelationalConventionSetBuilderDependencies relationalDependencies,
        IDuckLakeSingletonOptions? duckLakeSingletonOptions,
        IDuckDBEngineCapabilities? capabilities)
        : base(dependencies, relationalDependencies)
    {
        _ = duckLakeSingletonOptions;
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    /// <summary>
    ///     Builds and returns the convention set for the current database provider.
    /// </summary>
    /// <returns>The convention set for the current database provider.</returns>
    public override ConventionSet CreateConventionSet()
    {
        var conventionSet = base.CreateConventionSet();
        RemoveForeignKeyIndexConvention(conventionSet.EntityTypeBaseTypeChangedConventions);

        conventionSet.ForeignKeyAddedConventions.Clear();
        conventionSet.ForeignKeyAnnotationChangedConventions.Clear();
        conventionSet.ForeignKeyDependentRequirednessChangedConventions.Clear();
        conventionSet.ForeignKeyNullNavigationSetConventions.Clear();
        conventionSet.ForeignKeyOwnershipChangedConventions.Clear();
        conventionSet.ForeignKeyPrincipalEndChangedConventions.Clear();
        conventionSet.ForeignKeyPropertiesChangedConventions.Clear();
        conventionSet.ForeignKeyRemovedConventions.Clear();
        conventionSet.ForeignKeyRequirednessChangedConventions.Clear();
        conventionSet.ForeignKeyUniquenessChangedConventions.Clear();
        conventionSet.SkipNavigationForeignKeyChangedConventions.Clear();

        var valueGenerationConvention = new DuckDBValueGenerationConvention(
            Dependencies,
            RelationalDependencies,
            _capabilities);
        conventionSet.Replace<RelationalValueGenerationConvention>(valueGenerationConvention);
        conventionSet.ModelFinalizingConventions.Add(valueGenerationConvention);

        conventionSet.Replace<RuntimeModelConvention>(new DuckDBRuntimeModelConvention(Dependencies, RelationalDependencies));
        conventionSet.EntityTypeAddedConventions.Add(new DuckDBFileSourceConvention());

        return conventionSet;
    }

    private void RemoveForeignKeyIndexConvention(IList<IEntityTypeBaseTypeChangedConvention> conventions)
    {
        for (var i = conventions.Count - 1; i > -1; i--)
        {
            if (conventions[i] is ForeignKeyIndexConvention)
            {
                conventions.RemoveAt(i);
            }
        }
    }
}
