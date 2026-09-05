using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace DuckDB.EFCoreProvider.Metadata.Conventions;

/// <summary>Ensures owned values stored inline with their owner are materialized.</summary>
internal sealed class DuckDBOwnedValueConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        // Relationship conventions are suppressed by this provider. Restore inline owned values
        // without adding joins or separate-table collection auto-includes, which change set-operation shapes.
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var foreignKey in entityType.GetDeclaredForeignKeys())
            {
                if (foreignKey.IsOwnership
                    && (entityType.IsMappedToJson()
                        || (foreignKey.IsUnique
                            && entityType.GetTableName() is { } tableName
                            && tableName == foreignKey.PrincipalEntityType.GetTableName()
                            && entityType.GetSchema() == foreignKey.PrincipalEntityType.GetSchema())))
                {
                    foreignKey.PrincipalToDependent?.Builder.AutoInclude(true);
                }
            }
        }
    }
}