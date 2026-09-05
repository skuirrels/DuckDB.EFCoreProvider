using Microsoft.EntityFrameworkCore.Update;

namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBModificationCommand : ModificationCommand
{
    public DuckDBModificationCommand(in ModificationCommandParameters modificationCommandParameters) : base(in modificationCommandParameters)
    {
    }

    public DuckDBModificationCommand(in NonTrackedModificationCommandParameters modificationCommandParameters) : base(in modificationCommandParameters)
    {
    }

    /// <inheritdoc />
    public override IReadOnlyList<IColumnModification> ColumnModifications
    {
        get
        {
            var modifications = base.ColumnModifications;
            foreach (var modification in modifications)
            {
#if NET11_0_OR_GREATER
                var partialJsonUpdate = modification.JsonPath is { IsRoot: false };
#else
                var partialJsonUpdate = modification.JsonPath is not null and not "$";
#endif
                if (partialJsonUpdate)
                {
                    throw new InvalidOperationException(
                        "DuckDB does not support partial updates of owned JSON values. Replace the complete JSON document instead.");
                }
            }

            return modifications;
        }
    }
}