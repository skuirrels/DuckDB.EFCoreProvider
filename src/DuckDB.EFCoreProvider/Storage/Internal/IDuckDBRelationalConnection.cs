using Microsoft.EntityFrameworkCore.Storage;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public interface IDuckDBRelationalConnection : IRelationalConnection
{
    IDuckDBRelationalConnection CreateReadOnlyConnection();

    /// <summary>
    ///     Detaches the configured encrypted database from the host instance and invalidates the connection's
    ///     attachment state, so the next open re-attaches instead of assuming the catalog is still selected.
    /// </summary>
    /// <remarks>
    ///     The default implementation throws: it exists so adding this member does not break external
    ///     implementations of this internal-API interface, which package validation would otherwise reject.
    /// </remarks>
    void DetachEncryptedDatabase()
        => throw new NotSupportedException("This connection implementation does not support encrypted databases.");
}
