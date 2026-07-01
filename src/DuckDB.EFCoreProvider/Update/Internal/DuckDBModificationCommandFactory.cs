using Microsoft.EntityFrameworkCore.Update;

namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBModificationCommandFactory : IModificationCommandFactory
{
    public virtual IModificationCommand CreateModificationCommand(in ModificationCommandParameters modificationCommandParameters)
    {
        return new DuckDBModificationCommand(modificationCommandParameters);
    }

    public virtual INonTrackedModificationCommand CreateNonTrackedModificationCommand(in NonTrackedModificationCommandParameters modificationCommandParameters)
    {
        return new DuckDBModificationCommand(modificationCommandParameters);
    }
}
