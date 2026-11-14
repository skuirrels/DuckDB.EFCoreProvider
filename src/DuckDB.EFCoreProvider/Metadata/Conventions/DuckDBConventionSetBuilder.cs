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
        // Key discovery stays: it gives many-to-many join entities their composite key from the join foreign keys.
        RetainOnly(conventionSet.ForeignKeyPropertiesChangedConventions, convention => convention is KeyDiscoveryConvention);
        // A new foreign key takes its requiredness from its initial properties; this keeps it in step when the
        // relationship moves onto other properties. Cascade delete stays off, so delete behaviour is unchanged.
        conventionSet.ForeignKeyPropertiesChangedConventions.Add(new DuckDBForeignKeyRequirednessConvention());
        conventionSet.ForeignKeyRemovedConventions.Clear();
        conventionSet.ForeignKeyRequirednessChangedConventions.Clear();
        conventionSet.ForeignKeyUniquenessChangedConventions.Clear();
        RetainOnly(conventionSet.SkipNavigationForeignKeyChangedConventions, convention => convention is KeyDiscoveryConvention);

        var valueGenerationConvention = new DuckDBValueGenerationConvention(
            Dependencies,
            RelationalDependencies,
            _capabilities);
        conventionSet.Replace<RelationalValueGenerationConvention>(valueGenerationConvention);
        conventionSet.ModelFinalizingConventions.Add(valueGenerationConvention);

        conventionSet.Replace<RuntimeModelConvention>(new DuckDBRuntimeModelConvention(Dependencies, RelationalDependencies));
        conventionSet.EntityTypeAddedConventions.Add(new DuckDBFileSourceConvention());
        conventionSet.ModelFinalizingConventions.Add(new DuckDBStructFieldConvention());
        conventionSet.ModelFinalizingConventions.Add(new DuckDBOwnedValueConvention());

        return conventionSet;
    }

    private static void RetainOnly<TConvention>(IList<TConvention> conventions, Func<TConvention, bool> keep)
        where TConvention : IConvention
    {
        for (var i = conventions.Count - 1; i > -1; i--)
        {
            if (!keep(conventions[i]))
            {
                conventions.RemoveAt(i);
            }
        }
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