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
}
