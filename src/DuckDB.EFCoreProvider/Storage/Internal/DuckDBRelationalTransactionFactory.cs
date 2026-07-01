using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBRelationalTransactionFactory : RelationalTransactionFactory
{
    public DuckDBRelationalTransactionFactory(RelationalTransactionFactoryDependencies dependencies) : base(dependencies)
    {
    }

    /// <inheritdoc />
    public override RelationalTransaction Create(
        IRelationalConnection connection,
        DbTransaction transaction,
        Guid transactionId,
        IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
        bool transactionOwned)
    {
        return new DuckDBRelationalTransaction(
            connection,
            transaction,
            transactionId,
            logger,
            transactionOwned,
            Dependencies.SqlGenerationHelper);
    }
}
