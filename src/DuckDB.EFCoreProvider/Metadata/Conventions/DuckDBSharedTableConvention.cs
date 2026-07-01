using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace DuckDB.EFCoreProvider.Metadata.Conventions;

/// <summary>
///     A convention that manipulates names of database objects for entity types that share a table to avoid clashes.
/// </summary>
/// <remarks>
///     See <see href="https://aka.ms/efcore-docs-conventions">Model building conventions</see>.
/// </remarks>
public class DuckDBSharedTableConvention : SharedTableConvention
{
    /// <summary>
    /// Creates a new instance of <see cref="DuckDBSharedTableConvention"/>.
    /// </summary>
    /// <param name="dependencies">Parameter object containing dependencies for this convention.</param>
    /// <param name="relationalDependencies">Parameter object containing relational dependencies for this convention.</param>
    public DuckDBSharedTableConvention(ProviderConventionSetBuilderDependencies dependencies, RelationalConventionSetBuilderDependencies relationalDependencies) : base(dependencies, relationalDependencies)
    {
    }
}
