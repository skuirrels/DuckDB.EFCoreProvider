using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>Relationship configuration extensions for DuckDB STRUCT-backed foreign keys.</summary>
public static class DuckDBRelationshipBuilderExtensions
{
    /// <summary>
    ///     Configures a relationship whose dependent foreign-key value is stored in a mapped STRUCT leaf.
    /// </summary>
    /// <typeparam name="TPrincipalEntity">The principal entity type.</typeparam>
    /// <typeparam name="TDependentEntity">The dependent entity type.</typeparam>
    /// <param name="relationshipBuilder">The relationship builder.</param>
    /// <param name="foreignKeyExpression">
    ///     A nested dependent member path ending at the mapped STRUCT leaf, such as
    ///     <c>dependent =&gt; dependent.Relationship.ParentId</c>.
    /// </param>
    /// <returns>The same relationship builder for further configuration.</returns>
    /// <remarks>
    ///     The STRUCT complex property must already be configured with <c>UseStructMapping</c>. The provider creates
    ///     an internal shadow property for EF's relationship metadata and reuses the existing STRUCT field mapping.
    /// </remarks>
    public static ReferenceCollectionBuilder<TPrincipalEntity, TDependentEntity> HasStructForeignKey<
        TPrincipalEntity,
        TDependentEntity>(
        this ReferenceCollectionBuilder<TPrincipalEntity, TDependentEntity> relationshipBuilder,
        Expression<Func<TDependentEntity, object?>> foreignKeyExpression)
        where TPrincipalEntity : class
        where TDependentEntity : class
    {
        ArgumentNullException.ThrowIfNull(relationshipBuilder);

        Configure((ReferenceCollectionBuilder)relationshipBuilder, foreignKeyExpression);

        return relationshipBuilder;
    }

    /// <summary>
    ///     Configures a relationship without a typed collection navigation whose dependent foreign-key value is
    ///     stored in a mapped STRUCT leaf.
    /// </summary>
    /// <typeparam name="TDependentEntity">The dependent entity type.</typeparam>
    /// <param name="relationshipBuilder">The relationship builder.</param>
    /// <param name="foreignKeyExpression">
    ///     A nested dependent member path ending at the mapped STRUCT leaf.
    /// </param>
    /// <returns>The same relationship builder for further configuration.</returns>
    public static ReferenceCollectionBuilder HasStructForeignKey<TDependentEntity>(
        this ReferenceCollectionBuilder relationshipBuilder,
        Expression<Func<TDependentEntity, object?>> foreignKeyExpression)
        where TDependentEntity : class
    {
        ArgumentNullException.ThrowIfNull(relationshipBuilder);

        Configure(relationshipBuilder, foreignKeyExpression);

        return relationshipBuilder;
    }

    /// <summary>
    ///     Configures a one-to-one relationship whose dependent foreign-key value is stored in a mapped STRUCT leaf.
    /// </summary>
    /// <typeparam name="TDependentEntity">The dependent entity type of the relationship.</typeparam>
    /// <param name="relationshipBuilder">The one-to-one relationship builder.</param>
    /// <param name="foreignKeyExpression">
    ///     A nested dependent member path ending at the mapped STRUCT leaf, such as
    ///     <c>dependent =&gt; dependent.Relationship.ParentId</c>.
    /// </param>
    /// <returns>The same relationship builder for further configuration.</returns>
    /// <remarks>
    ///     The dependent entity type is given explicitly, mirroring EF's
    ///     <c>HasForeignKey&lt;TDependentEntity&gt;</c>, so a relationship configured from either the principal or
    ///     the dependent side is unambiguous even when both entity types expose the same STRUCT path.
    /// </remarks>
    public static ReferenceReferenceBuilder HasStructForeignKey<TDependentEntity>(
        this ReferenceReferenceBuilder relationshipBuilder,
        Expression<Func<TDependentEntity, object?>> foreignKeyExpression)
        where TDependentEntity : class
    {
        ArgumentNullException.ThrowIfNull(relationshipBuilder);

        var binding = CreateBinding(foreignKeyExpression);
        relationshipBuilder
            .HasForeignKey(typeof(TDependentEntity), binding.ShadowPropertyName)
            .HasAnnotation(DuckDBAnnotationNames.StructForeignKeyPath, binding);

        return relationshipBuilder;
    }

    private static void Configure<TDependentEntity>(
        ReferenceCollectionBuilder relationshipBuilder,
        Expression<Func<TDependentEntity, object?>> foreignKeyExpression)
        where TDependentEntity : class
    {
        var binding = CreateBinding(foreignKeyExpression);
        relationshipBuilder
            .HasForeignKey(binding.ShadowPropertyName)
            .HasAnnotation(DuckDBAnnotationNames.StructForeignKeyPath, binding);
    }

    private static DuckDBStructForeignKeyPath CreateBinding<TDependentEntity>(
        Expression<Func<TDependentEntity, object?>> foreignKeyExpression)
        where TDependentEntity : class
        => DuckDBStructForeignKeyPath.Create(
            DuckDBStructPropertyBuilderExtensions.GetMemberPath(foreignKeyExpression));
}