using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Internal;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace DuckDB.EFCoreProvider.Metadata.Conventions;

/// <summary>
///     A convention that configures store value generation as <see cref="ValueGenerated.OnAdd" /> on properties that are
///     part of the primary key and not part of any foreign keys, were configured to have a database default value
///     or were configured to use a <see cref="DuckDBValueGenerationStrategy" />.
///     It also configures properties as <see cref="ValueGenerated.OnAddOrUpdate" /> if they were configured as computed columns.
/// </summary>
/// <remarks>
///     See <see href="https://aka.ms/efcore-docs-conventions">Model building conventions</see>.
///     for more information and examples.
/// </remarks>
public class DuckDBValueGenerationConvention :
    RelationalValueGenerationConvention,
    IModelFinalizingConvention
{
    private readonly IDuckDBEngineCapabilities _capabilities;

    /// <summary>
    ///     Creates a new instance of <see cref="DuckDBValueGenerationConvention" />.
    /// </summary>
    /// <param name="dependencies">Parameter object containing dependencies for this convention.</param>
    /// <param name="relationalDependencies">Parameter object containing relational dependencies for this convention.</param>
    public DuckDBValueGenerationConvention(
        ProviderConventionSetBuilderDependencies dependencies,
        RelationalConventionSetBuilderDependencies relationalDependencies)
        : this(dependencies, relationalDependencies, false)
    {
    }

    /// <summary>Creates a value-generation convention for the selected backend.</summary>
    /// <param name="dependencies">Parameter object containing dependencies for this convention.</param>
    /// <param name="relationalDependencies">Parameter object containing relational dependencies for this convention.</param>
    /// <param name="isDuckLake">Whether store generation must be limited to DuckLake-compatible behavior.</param>
    public DuckDBValueGenerationConvention(
        ProviderConventionSetBuilderDependencies dependencies,
        RelationalConventionSetBuilderDependencies relationalDependencies,
        bool isDuckLake)
        : this(dependencies, relationalDependencies, new DuckDBEngineCapabilities(isDuckLake))
    {
    }

    /// <summary>Creates a value-generation convention for the selected engine capabilities.</summary>
    /// <param name="dependencies">Parameter object containing dependencies for this convention.</param>
    /// <param name="relationalDependencies">Parameter object containing relational dependencies for this convention.</param>
    /// <param name="capabilities">Capabilities that determine supported store-generated values.</param>
    public DuckDBValueGenerationConvention(
        ProviderConventionSetBuilderDependencies dependencies,
        RelationalConventionSetBuilderDependencies relationalDependencies,
        IDuckDBEngineCapabilities capabilities)
        : base(dependencies, relationalDependencies)
    {
        _capabilities = capabilities;
    }

    /// <summary>
    ///     Called after an annotation is changed on a property.
    /// </summary>
    /// <param name="propertyBuilder">The builder for the property.</param>
    /// <param name="name">The annotation name.</param>
    /// <param name="annotation">The new annotation.</param>
    /// <param name="oldAnnotation">The old annotation.</param>
    /// <param name="context">Additional information associated with convention execution.</param>
    public override void ProcessPropertyAnnotationChanged(
        IConventionPropertyBuilder propertyBuilder,
        string name,
        IConventionAnnotation? annotation,
        IConventionAnnotation? oldAnnotation,
        IConventionContext<IConventionAnnotation> context)
    {
        if (name == DuckDBAnnotationNames.ValueGenerationStrategy)
        {
            propertyBuilder.ValueGenerated(GetValueGenerated(propertyBuilder.Metadata));
            return;
        }

        base.ProcessPropertyAnnotationChanged(propertyBuilder, name, annotation, oldAnnotation, context);
    }

    /// <summary>
    ///     Returns the store value generation strategy to set for the given property.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns>The store value generation strategy to set for the given property.</returns>
    protected override ValueGenerated? GetValueGenerated(IConventionProperty property)
        => GetValueGenerationStrategy(property) switch
        {
            DuckDBValueGenerationStrategy.AutoIncrement => ValueGenerated.OnAdd,
            _ => base.GetValueGenerated(property)
        };

    /// <summary>
    /// Finalizes the model by configuring value generation strategies for properties
    /// based on their annotations and characteristics.
    /// </summary>
    /// <param name="modelBuilder">The model builder used to build and configure the model.</param>
    /// <param name="context">The context in which the model finalizing operation is performed.</param>
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var property in entityType.GetDeclaredProperties())
            {
                var annotation = property.FindAnnotation(DuckDBAnnotationNames.ValueGenerationStrategy);
                if (annotation?.Value != null)
                {
                    continue;
                }

                if (!_capabilities.SupportsSequences)
                {
                    if (property.ValueGenerated == ValueGenerated.OnAdd
                        && DuckDBValueGenerationStrategyCompatibility.IsAutoIncrementCompatible(property.ClrType)
                        && property.GetValueGeneratorFactory() is null
                        && property.FindAnnotation(RelationalAnnotationNames.DefaultValue) == null
                        && property.FindAnnotation(RelationalAnnotationNames.DefaultValueSql) == null)
                    {
                        property.Builder.ValueGenerated(ValueGenerated.Never);
                        property.SetValueGenerationStrategy(DuckDBValueGenerationStrategy.None);
                    }

                    continue;
                }

                if (property.ValueGenerated == ValueGenerated.OnAdd
                    && DuckDBValueGenerationStrategyCompatibility.IsAutoIncrementCompatible(property.ClrType)
                    && !HasConverter(property)
                    && property.FindAnnotation(RelationalAnnotationNames.DefaultValue) == null
                    && property.FindAnnotation(RelationalAnnotationNames.DefaultValueSql) == null
                    && property.FindAnnotation(RelationalAnnotationNames.ComputedColumnSql) == null)
                {
                    property.SetValueGenerationStrategy(DuckDBValueGenerationStrategy.AutoIncrement);
                }
            }
        }
    }

    private static DuckDBValueGenerationStrategy GetValueGenerationStrategy(IConventionProperty property)
        => property.GetValueGenerationStrategy();

    private static bool HasConverter(IConventionProperty property)
        => property.FindTypeMapping()?.Converter != null
           || property.GetValueConverter() != null;
}