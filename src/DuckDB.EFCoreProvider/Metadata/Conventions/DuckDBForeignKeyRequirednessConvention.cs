using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace DuckDB.EFCoreProvider.Metadata.Conventions;

/// <summary>
///     Makes a relationship required when it moves onto non-nullable foreign key properties, and optional when it
///     moves onto nullable ones.
/// </summary>
/// <remarks>
///     The provider removes EF Core's foreign key property discovery from foreign key events, which is where EF
///     infers requiredness. Restoring that whole convention would also rewrite the CLR types of shadow foreign key
///     properties, and so the nullability of existing columns. This convention restores only the requiredness
///     inference. Explicit and data annotation configuration still win, and ownerships are left unchanged.
/// </remarks>
internal sealed class DuckDBForeignKeyRequirednessConvention : IForeignKeyPropertiesChangedConvention
{
    public void ProcessForeignKeyPropertiesChanged(
        IConventionForeignKeyBuilder relationshipBuilder,
        IReadOnlyList<IConventionProperty> oldDependentProperties,
        IConventionKey oldPrincipalKey,
        IConventionContext<IReadOnlyList<IConventionProperty>> context)
    {
        var foreignKey = relationshipBuilder.Metadata;
        if (!foreignKey.IsInModel || foreignKey.IsOwnership)
        {
            return;
        }

        // A convention-level call is ignored when requiredness was configured explicitly or by data annotation.
        relationshipBuilder.IsRequired(!foreignKey.Properties.Any(property => property.IsNullable));
    }
}