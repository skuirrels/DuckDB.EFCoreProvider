using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Query.Internal;
using Microsoft.EntityFrameworkCore.Query;

namespace DuckDB.EFCoreProvider.NTS.Query.Internal;

/// <summary>
/// Factory for <see cref="DuckDBNtsQuerySqlGenerator"/> which wraps geometry projections
/// in <c>ST_AsWKT()</c> so that DuckDB.NET can read them as plain VARCHAR strings.
/// </summary>
public class DuckDBNtsQuerySqlGeneratorFactory : DuckDBQuerySqlGeneratorFactory
{
    /// <inheritdoc cref="DuckDBQuerySqlGeneratorFactory(QuerySqlGeneratorDependencies, IDuckDBSingletonOptions)"/>
    public DuckDBNtsQuerySqlGeneratorFactory(
        QuerySqlGeneratorDependencies dependencies,
        IDuckDBSingletonOptions duckDbSingletonOptions)
        : base(dependencies, duckDbSingletonOptions)
    {
        _dependencies = dependencies;
        _options = duckDbSingletonOptions;
    }

    private readonly QuerySqlGeneratorDependencies _dependencies;
    private readonly IDuckDBSingletonOptions _options;

    /// <inheritdoc />
    public override QuerySqlGenerator Create()
        => new DuckDBNtsQuerySqlGenerator(_dependencies, _options.ReverseNullOrderingEnabled);
}

